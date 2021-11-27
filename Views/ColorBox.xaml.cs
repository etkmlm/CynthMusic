/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using CynthMusic.Management;

namespace CynthMusic.Views
{
    /// <summary>
    /// Interaction logic for ListBox.xaml
    /// </summary>
    public partial class ColorBox : Window
    {
        public ColorBox(Color c)
        {
            InitializeComponent();

            rctColor.Fill = new SolidColorBrush(c);
            txtRED.Text = c.R.ToString();
            txtGREEN.Text = c.G.ToString();
            txtBLUE.Text = c.B.ToString();
            txtRED.MouseWheel += Wheel;
            txtGREEN.MouseWheel += Wheel;
            txtBLUE.MouseWheel += Wheel;

            btnExit.Click += (a, b) => Close();
            btnApply.Click += (a, s) =>
            {
                try
                {
                    byte r = byte.Parse(txtRED.Text);
                    byte g = byte.Parse(txtGREEN.Text);
                    byte b = byte.Parse(txtBLUE.Text);

                    if (r > 255 || g > 255 || b > 255)
                        throw new Exception();

                    Settings.genColor = Color.FromRgb(r, g, b);
                    Close();
                }
                catch (Exception)
                {
                    new AlertBox("Değerler 255'ten büyük, 0'dan küçük olmamalı ve sayı olmalıdır.", "Hata").ShowDialog();
                }
            };
            txtRED.Focus();
        }

        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            TextBox txt = sender as TextBox;
            int index = int.Parse(txt.Text);
            if (index is > 255 or < 0)
                return;
            index += e.Delta > 0 ? 1 : -1;
            txt.Text = index.ToString();
        }

        public void TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                byte r = byte.Parse(txtRED.Text);
                byte g = byte.Parse(txtGREEN.Text);
                byte b = byte.Parse(txtBLUE.Text);
                if (r <= 255 && g <= 255 && b <= 255)
                    rctColor.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            catch (Exception)
            {

            }
        }
    }
}
