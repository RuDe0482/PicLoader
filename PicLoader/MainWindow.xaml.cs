using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PicLoader
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly CancellationTokenSource?[] _cts = new CancellationTokenSource?[3];

        private TextBox[] _textBoxes;
        private Image[] _images;

        public MainWindow()
        {
            InitializeComponent();

            // Инициализация массивов
            _textBoxes = new[] { UrlTextBox1, UrlTextBox2, UrlTextBox3 };
            _images = new[] { ResultImage1, ResultImage2, ResultImage3 };
        }

        private async Task StartLoad(int index)
        {
            string url = _textBoxes[index].Text;

            _cts[index]?.Cancel();
            _cts[index]?.Dispose();

            _cts[index] = new CancellationTokenSource();

            await LoadImageFromUrlAsync(url.Trim(), _images[index], _cts[index]!.Token);
        }

        // Один обработчик для всех кнопок Load
        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
            {
                await StartLoad(index);
            }
        }

        // Один обработчик для всех Stop
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
            {
                _cts[index]?.Cancel();
                _cts[index]?.Dispose();
                _cts[index] = null;

                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = 0;
            }
        }

        private async void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            int index = Array.IndexOf(_textBoxes, sender);
            if (index >= 0)
                await StartLoad(index);
        }

        // Load All (ступенчатый прогресс)
        private async void LoadButton4_Click(object sender, RoutedEventArgs e)
        {
            int total = _textBoxes.Count(tb => !string.IsNullOrWhiteSpace(tb.Text));
            if (total == 0) return;

            int completed = 0;

            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 0;

            for (int i = 0; i < _textBoxes.Length; i++)
            {
                string url = _textBoxes[i].Text;

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                _cts[i]?.Cancel();
                _cts[i]?.Dispose();
                _cts[i] = new CancellationTokenSource();

                try
                {
                    await LoadImageFromUrlAsync(
                        url.Trim(),
                        _images[i],
                        _cts[i]!.Token,
                        useStepProgress: true);

                    completed++;
                    DownloadProgressBar.Value = (double)completed / total * 100;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task LoadImageFromUrlAsync(
            string url,
            Image targetImage,
            CancellationToken cancellationToken,
            bool useStepProgress = false)
        {
            if (!useStepProgress)
            {
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.IsIndeterminate = false;
            }

            if (string.IsNullOrWhiteSpace(url))
                return;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show("Неверный URL. Поддерживается только http/https.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;

                if (!useStepProgress)
                    DownloadProgressBar.IsIndeterminate = contentLength == null || contentLength <= 0;

                await using var responseStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                var ms = new MemoryStream();
                var buffer = new byte[81920];

                long totalRead = 0;
                int read;

                while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await ms.WriteAsync(buffer, 0, read, cancellationToken);
                    totalRead += read;

                    if (!useStepProgress && contentLength != null && contentLength > 0)
                    {
                        double percent = (double)totalRead / contentLength.Value * 100;
                        DownloadProgressBar.Value = percent;
                    }
                }

                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                targetImage.Source = bitmap;

                if (!useStepProgress)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Value = 100;
                }
            }
            catch (OperationCanceledException)
            {
                if (!useStepProgress)
                {
                    DownloadProgressBar.Value = 0;
                    DownloadProgressBar.IsIndeterminate = false;
                }
                throw;
            }
            catch (Exception ex)
            {
                if (!useStepProgress)
                {
                    DownloadProgressBar.Value = 0;
                    DownloadProgressBar.IsIndeterminate = false;
                }

                MessageBox.Show("Ошибка загрузки: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}