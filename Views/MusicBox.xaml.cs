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
using System.IO;
using Path = System.IO.Path;
using Microsoft.Win32;
using CynthMusic.Management;
using System.Threading.Tasks;

namespace CynthMusic.Views
{
    /// <summary>
    /// Interaction logic for MusicBox.xaml
    /// </summary>
    public partial class MusicBox : Window
    {
        private IMusicList list;
        private IEnumerable<IMusic> musics;
        private OrderableCollection<Selectable<IMusic>> srcMusics;
        private PlaylistManager playlistManager => MainWindow.playlistManager;
        private AddonManager addonManager => MainWindow.addonManager;

        public MusicBox(IMusicList list, int id = -1)
        {
            InitializeComponent();

            if (id != -1)
                list = playlistManager.GetAsync(id).ConfigureAwait(false).GetAwaiter().GetResult();

            if (list.Musics == null)
                list.Musics = Enumerable.Empty<IMusic>();

            MouseLeftButtonDown += (a, b) => DragMove();
            btnExit.Click += (a, b) => Close();
            txtLocation.KeyDown += (a, b) =>
            {
                if (b.Key != Key.Enter)
                    return;
                AddMusic(txtLocation.Text);
                txtLocation.Clear();
            };
            btnSelectLocation.Click += (a, b) =>
            {
                var dialog = new OpenFileDialog
                {
                    Multiselect = true,
                    CheckPathExists = true,
                    Filter = "MP3 Dosyaları|*.mp3|M4A Dosyaları|*.m4a"
                };
                bool? value = dialog.ShowDialog();
                if (!value.HasValue || !value.Value)
                    return;
                if (dialog.FileNames.Length > 0)
                    foreach (string name in dialog.FileNames)
                        AddMusic(name);
                else
                    AddMusic(dialog.FileName);
            };
            lvMusics.PreviewMouseLeftButtonUp += (a, b) =>
                lvMusics.SelectedIndex = -1;
            btnApply.Click += async (a, b) =>
            {
                var musics = srcMusics.Where(x => x.Item.IsSelected).Select(x => x.Item.Item);
                await playlistManager.AddMusicsAsync(list, musics);
                Close();
            };

            this.list = list;
            musics = playlistManager.GetAllMusicsFromLocations().ToEnumerable();
            srcMusics = new OrderableCollection<Selectable<IMusic>>();

            Loaded += async (a, b) => await lvMusics.Dispatcher.InvokeAsync(() =>
            {
                foreach (var x in musics.Where(x => !list.Musics.Contains(x)))
                    srcMusics.Add(new Selectable<IMusic>(x, false));

                lvMusics.ItemsSource = srcMusics;
            });

            txtSearch.TextChanged += (a, b) =>
            {
                string q = txtSearch.Text.ToLower();
                if (string.IsNullOrWhiteSpace(q))
                    lvMusics.ItemsSource = srcMusics;
                else
                    lvMusics.ItemsSource = srcMusics.Where(x => x.Item.Item.Name.ToLower().Contains(q) || x.Item.Item.Author.ToLower().Contains(q));
            };
        }

        private async void AddMusic(string location, IMusic music = null)
        {
            if (music == null)
            {
                if (!File.Exists(location))
                {
                    var x = await addonManager.GetMusicAsync(location, true);
                    if (x == null)
                    {
                        new YouTubeSelectBox(location, (a) => AddMusic(location, a)).ShowDialog();
                        return;
                    }
                    music = x;
                }
                else
                {
                    string extension = Path.GetExtension(location);
                    if (!CynthCore.MusicService.CheckExtension(extension.ToLower()))
                        return;
                    music = await addonManager.GetMusicAsync(location, false);
                }
            }
            if (music == null || list.Musics.Any(x => x.Equals(music)) || srcMusics.Any(x => x.Equals(music)))
                return;
            srcMusics.Add(new Selectable<IMusic>(music, true));
            lvMusics.ScrollIntoView(srcMusics.LastOrDefault());
        }

        private async void CheckedChanged(object sender, RoutedEventArgs e)
        {
            CheckBox box = sender as CheckBox;
            var item = srcMusics.ObjectConvert(box.DataContext).Value;
            var i = item.Item;
            i.IsSelected = box.IsChecked.Value;
            item.Item = i;
            srcMusics.SetItem(item.Index - 1, item);
            await Dispatcher.InvokeAsync(() => chkAll.IsChecked = srcMusics.All(x => x.Item.IsSelected));
        }

        private async void CheckedChangedAll(object sender, RoutedEventArgs e) => await Dispatcher.InvokeAsync(() =>
        {
            CheckBox chk = sender as CheckBox;
            bool c = chk.IsChecked.Value;
            for (int i = 0; i < srcMusics.Count; i++)
            {
                var x = srcMusics[i];
                var s = x.Item;
                s.IsSelected = c;
                x.Item = s;
                srcMusics.SetItem(i, x);
            }
            lvMusics.Items.Refresh();
        });
    }
}
