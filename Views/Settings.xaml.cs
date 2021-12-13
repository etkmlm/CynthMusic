/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore.Entities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CynthMusic.Views
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Window
    {
        private ConfigService service => MainWindow.configService;
        private MainWindow window = (MainWindow)App.Current.MainWindow;
        public static Color genColor;
        public static Color sldColor;
        private readonly Color defBack = Color.FromRgb(41, 41, 41);
        private readonly OrderableCollection<Selectable<string>> srcProperties;
        private readonly List<string> properties;

        public Settings()
        {
            InitializeComponent();

            MouseDown += (a, b) => DragMove();
            btnExit.Click += (a, b) => Close();

            srcProperties = new OrderableCollection<Selectable<string>>();
            lvProperties.ItemsSource = srcProperties;
            properties = new List<string>();

            rctBG.PreviewMouseLeftButtonDown += (a, b) =>
            {
                new ColorBox(genColor, true).ShowDialog();
                rctBG.Background = new SolidColorBrush(genColor);
                service.Set("BGENERAL", $"{genColor.R},{genColor.G},{genColor.B}");
                service.Set("BACKGROUND", null);
                imgBack.Background = new SolidColorBrush(defBack);
                window.RefreshTheme();
            };

            rctSlider.PreviewMouseLeftButtonDown += (a, b) =>
            {
                new ColorBox(sldColor, false).ShowDialog();
                rctSlider.Background = new SolidColorBrush(sldColor);
                service.Set("BGSLIDER", $"{sldColor.R},{sldColor.G},{sldColor.B}");
                window.RefreshTheme();
            };

            chkBack.Checked += (a, b) =>
            {
                rctBG.IsEnabled = false;
                imgBack.IsEnabled = true;
            };

            chkSpecial.Checked += (a, b) =>
            {
                rctBG.IsEnabled = true;
                imgBack.IsEnabled = false;
            };

            imgBack.PreviewMouseLeftButtonDown += (a, b) =>
            {
                OpenFileDialog dialog = new()
                {
                    Filter = "Resim Dosyaları (*.png, *.jpg, *.jpeg)|*.png;*.jpg;*.jpeg",
                    Title = "Bir Resim Seç"
                };
                var result = dialog.ShowDialog();
                if (!result.HasValue || !result.Value)
                    return;
                string file = dialog.FileName;
                service.Set("BACKGROUND", file);
                imgBack.Background = new ImageBrush(new BitmapImage(new Uri(file)));
                window.RefreshTheme();
            };

            sldOpacity.ValueChanged += (a, b) =>
            {
                service.Set("BGOPACITY", ((int)sldOpacity.Value).ToString());
                window.panelListing.Opacity = sldOpacity.Value / 100;
            };

            chkBlue.Checked += (a, b) =>
            {
                service.Set("PLAYERTHEME", "1");
                window.RefreshTheme();
            };

            chkPurple.Checked += (a, b) =>
            {
                service.Set("PLAYERTHEME", "2");
                window.RefreshTheme();
            };

            chkBlack.Checked += (a, b) =>
            {
                service.Set("PLAYERTHEME", "3");
                window.RefreshTheme();
            };

            RefreshSettings();
        }

        public void RefreshSettings()
        {
            string back = service.Get("BACKGROUND");
            bool isImage = !string.IsNullOrWhiteSpace(back);
            chkSpecial.IsChecked = !isImage;
            chkBack.IsChecked = isImage;
            if (MainWindow.IsValidImage(back))
                imgBack.Background = new ImageBrush(new BitmapImage(new Uri(back)));
            else
                imgBack.Background = new SolidColorBrush(defBack);
            genColor = window.GetColor("BGENERAL").Value;
            rctBG.Background = new SolidColorBrush(genColor);
            sldColor = window.GetColor("BGSLIDER").Value;
            rctSlider.Background = new SolidColorBrush(sldColor);
            sldOpacity.Value = int.Parse(service.Get("BGOPACITY"));
            string id = service.Get("PLAYERTHEME");
            chkBlue.IsChecked = id == "1";
            chkPurple.IsChecked = id == "2";
            chkBlack.IsChecked = id == "3";

            srcProperties.Clear();
            foreach(var x in service.GetBooleanProperties().Where(x => service.HasPreview(x)))
            {
                bool value = service.Get(x).ToLower() == "true";
                srcProperties.Add(new Selectable<string>(service.GetPreview(x), value));
                properties.Add(x);
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox chk = sender as CheckBox;
            var item = srcProperties.ObjectConvert(chk.DataContext).Value;
            var i = item.Item;
            i.IsSelected = chk.IsChecked.Value;
            item.Item = i;
            srcProperties.SetItem(item.Index - 1, item);
            service.Set(properties[item.Index - 1], i.IsSelected ? "TRUE" : "FALSE");
        }
    }
}
