/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CynthMusic.Views
{
    /// <summary>
    /// Interaction logic for AlertBox.xaml
    /// </summary>
    public partial class AlertBox : Window
    {
        public static readonly Brush ALERT_COLOR = new LinearGradientBrush(Color.FromRgb(47, 47, 47), Color.FromRgb(35, 35, 35), 45);
        public AlertBox(string title, string message, bool isResulted = false)
        {
            InitializeComponent();
            btnExit.Click += (a, b) => Execute(false);
            Title = title;
            Background = ALERT_COLOR;
            lblTitle.Content = title;
            txtMessage.Text = message;

            btnYes.Click += (a, b) => Execute(true);
            btnNo.Click += (a, b) => Execute(false);

            btnYes.Visibility = btnNo.Visibility = isResulted ? Visibility.Visible : Visibility.Hidden;
            btnYes.Content = MainWindow.translator.Get("yes");
            btnNo.Content = MainWindow.translator.Get("no");
        }

        private void Execute(bool result)
        {
            DialogResult = result;
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
