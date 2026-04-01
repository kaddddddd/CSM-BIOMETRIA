using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using CSMBiometricoWPF.Biometria;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class PruebaLectorPage : Page
    {
        private ServicioBiometrico? _biometrico;

        public PruebaLectorPage()
        {
            InitializeComponent();

            _biometrico = ServicioBiometrico.Compartido;
            _biometrico.OnCambioEstado    += OnCambioEstado;
            _biometrico.OnMensaje         += OnMensaje;
            _biometrico.OnImagenCapturada += OnImagenCapturada;

            Unloaded += (_, _) =>
            {
                _biometrico.OnCambioEstado    -= OnCambioEstado;
                _biometrico.OnMensaje         -= OnMensaje;
                _biometrico.OnImagenCapturada -= OnImagenCapturada;
                _biometrico.DetenerCaptura();
            };
        }

        private void OnCambioEstado(object? sender, EstadoLector estado)
            => Dispatcher.Invoke(() => lblEstadoLector.Text = estado.ToString());

        private void OnMensaje(object? sender, string msg)
            => Dispatcher.Invoke(() => lblMensaje.Text = msg);

        private void OnImagenCapturada(object? sender, System.Drawing.Bitmap bmp)
            => Dispatcher.Invoke(() => imgHuella.Source = BitmapToImageSource(bmp));

        private void BtnIniciar_Click(object sender, RoutedEventArgs e)
        {
            btnIniciar.IsEnabled = false;
            btnDetener.IsEnabled = true;
            lblEstado.Text = "Inicializando lector...";
            IniciarAsync();
        }

        private async void IniciarAsync()
        {
            try
            {
                bool ok = _biometrico.EstaListo || await _biometrico.InicializarAsync();
                if (ok)
                {
                    _biometrico.IniciarCaptura(ServicioBiometrico.ModoCaptura.Prueba);
                    Dispatcher.Invoke(() => lblEstado.Text = "Listo — coloque su dedo en el sensor.");
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        lblEstado.Text = "No se pudo inicializar el lector.";
                        btnIniciar.IsEnabled = true;
                        btnDetener.IsEnabled = false;
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    lblMensaje.Text = "Error: " + ex.Message;
                    btnIniciar.IsEnabled = true;
                    btnDetener.IsEnabled = false;
                });
            }
        }

        private void BtnDetener_Click(object sender, RoutedEventArgs e)
        {
            _biometrico?.DetenerCaptura();
            btnIniciar.IsEnabled = true;
            btnDetener.IsEnabled = false;
            lblEstado.Text = "Captura detenida.";
        }

        private static ImageSource BitmapToImageSource(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}
