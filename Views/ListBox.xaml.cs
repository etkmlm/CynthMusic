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
    public partial class ListBox : Window
    {
        private const string holderURL = "YouTube Linki (İsteğe Bağlı)", holderName = "İsim";
        private IMusicList loadedYouTubeList;
        private PlaylistManager manager => MainWindow.playlistManager;
        public ListBox()
        {
            InitializeComponent();
            txtName.Text = holderName;
            txtURL.Text = holderURL;

            MouseDown += (a, b) => DragMove();

            txtName.GotFocus += (a, b) =>
            {
                if (txtName.Text == holderName)
                    txtName.Clear();
            };
            txtName.LostFocus += (a, b) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                    txtName.Text = holderName;
            };
            txtURL.GotFocus += (a, b) =>
            {
                if (txtURL.Text == holderURL)
                    txtURL.Clear();
            };
            txtURL.LostFocus += (a, b) =>
            {
                if (string.IsNullOrWhiteSpace(txtURL.Text))
                    txtURL.Text = holderURL;
            };
            btnExit.Click += (a, b) => Close();

            txtURL.KeyDown += async (a, b) =>
            {
                if (b.Key == Key.Enter)
                    await LoadYT();
            };
            txtName.KeyDown += async (a, b) =>
            {
                if (b.Key == Key.Enter)
                    await Apply();
            };

            btnApplyURL.Click += async (a, b) => await LoadYT();

            btnApply.Click += async (a, b) => await Apply();

            txtName.Focus();
        }

        private async System.Threading.Tasks.Task LoadYT()
        {
            var list = await manager.GetYouTubeListAsync(txtURL.Text, manager.GetAlgCheck(), manager.GetAlgAuthors());
            if (list == null)
            {
                new AlertBox("Hata", "Liste geçersiz.").ShowDialog();
                txtURL.Clear();
                txtURL.Background = new SolidColorBrush(Color.FromRgb(255, 22, 25));
                return;
            }
            loadedYouTubeList = list;
            txtURL.Text = list.Value.Name;
            txtURL.Background = new SolidColorBrush(Colors.Green);
        }

        private async System.Threading.Tasks.Task Apply()
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                new AlertBox("Hata", "Lütfen geçerli bir liste ismi girin.").ShowDialog();
                return;
            }
            if (await manager.Contains(txtName.Text))
            {
                new AlertBox("Hata", "Aynı isme sahip bir liste zaten bulunuyor.").ShowDialog();
                return;
            }

            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (loadedYouTubeList != null)
                {
                    loadedYouTubeList.Name = txtName.Text;
                    await manager.AddPlaylistAsync(loadedYouTubeList);
                }
                else
                {
                    int id = await manager.AddPlaylistAsync(new MusicList(-1, txtName.Text, Environment.UserName));
                    new MusicBox(null, id).ShowDialog();
                }
                Close();
            });
        }

    }
}
