/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CoreliumYouTubeKit;
using CoreliumYouTubeKit.Entities;
using CynthCore;
using CynthCore.Entities;
using CynthMusic.Management;
using System;
using System.Collections.Generic;
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

namespace CynthMusic.Views
{
    /// <summary>
    /// Interaction logic for YouTubeSelectBox.xaml
    /// </summary>
    public partial class YouTubeSelectBox : Window
    {
        private PlaylistManager manager => MainWindow.playlistManager;

        private IEnumerable<YouTubeMusic> musics;
        public YouTubeSelectBox(string query, Action<YouTubeMusic> Select)
        {
            InitializeComponent();

            musics = Enumerable.Empty<YouTubeMusic>();
            Loaded += async (a, b) =>
            {
                await foreach(var x in manager.GetMusicsFromQuery(query))
                    musics = musics.Append(x);
                lvMusics.ItemsSource = musics;
            };

            MouseDown += (a, b) => DragMove();

            btnApply.Click += (a, b) =>
            {
                if (lvMusics.SelectedIndex == -1)
                    return;
                Select(musics.ElementAt(lvMusics.SelectedIndex));
                Close();
            };
            btnExit.Click += (a, b) => Close();
        }
    }
}
