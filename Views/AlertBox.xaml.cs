/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using System;
using System.Collections.Generic;
using System.Text;
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
    /// Interaction logic for AlertBox.xaml
    /// </summary>
    public partial class AlertBox : Window
    {
        public static readonly Brush ALERT_COLOR = new LinearGradientBrush(Color.FromRgb(47, 47, 47), Color.FromRgb(35, 35, 35), 45);
        public AlertBox(string title, string message)
        {
            InitializeComponent();
            btnExit.Click += (a, b) => Close();
            Title = title;
            Background = ALERT_COLOR;
            lblTitle.Content = title;
            txtMessage.Text = message;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
