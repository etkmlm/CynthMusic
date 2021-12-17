/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CynthMusic.Views
{
    /// <summary>
    /// Interaction logic for DownloadBox.xaml
    /// </summary>
    public partial class DownloadBox : Window
    {
        private readonly Downloader downloader;
        private static readonly string DOWNLOADER_LOCATION = Environment.CurrentDirectory + "\\CoreliumUpdater.exe";
        public DownloadBox(string link, string destination)
        {
            InitializeComponent();
            Background = AlertBox.ALERT_COLOR;
            downloader = new Downloader();
            btnExit.Click += (a, b) => Environment.Exit(0);
            downloader.ProgressChanged += (a) =>
            {
                progress.Value = a;
                lblState.Content = "%" + a;
            };
            downloader.DownloadFinished += () =>
            {
                if (!File.Exists(DOWNLOADER_LOCATION))
                    new AlertBox("Hata", "Güncelleyici bulunamadı, güncelleme manuel yapılmalıdır.").ShowDialog();
                else
                    Process.Start(DOWNLOADER_LOCATION, "CynthMusic");
                Environment.Exit(0);
            };

            Download(link, destination);
        }

        private async void Download(string link, string destination) =>
            await downloader.Download(link, destination);

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
