/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore;
using CynthMusic.Views;
using CynthCore.Entities;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CynthMusic.Management;
using System.Windows.Data;
using System.Linq;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using CynthCore.Utils;
using System.Media;
using System.Reflection;

namespace CynthMusic
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static bool DEBUG_MODE;

        public static AddonManager addonManager;
        public static PlayerService playerService;
        public static PlaylistManager playlistManager;
        
        public static readonly ConfigService configService = new()
        {
            { "LASTID", "" },
            { "BTOP", "42,47,47" },
            { "BGENERAL", "41,41,41" },
            { "BGOPACITY", "100" },
            { "BACKGROUND", "" },
            { "PLAYERTHEME", "1" },
            { "DEBUG", "FALSE" },
            { "VOL", "50,50" },
            { "PLINTRAY", "FALSE", "Program açıldığında oynatmak için tepsiye küçült" },
            { "FAUTH", "FALSE", "Şarkı isimlerinden sanatçıyı otomatik bul" },
            { "STM", "TRUE", "Ses kısıldığında şarkıyı otomatik durdur" },
            { "FIRST", "TRUE" }
        };
        private readonly MusicService musicService;

        private readonly object[,] menus = new object[5, 4]
        {
            { "btnListNow", "lvPlaying", false, false }, { "btnListLists", "lvPlaylists", true, false }, { "lvPlaylists", "lvPlaylist", true, true }, { "btnListFavourites", "lvFavourites", false, true }, { "btnExplore", "lvLocations", true, false }
        };

        private int switchedMenu;
        private string switchedLocationIdentity;
        private int listIdToRename = -1;
        public double volume = 50;
        private float systemVolume = 0;

        private readonly MMDeviceEnumerator enumerator;
        private readonly MMDevice device;
        private readonly InteropService interop;
        private readonly UpdateService update;

        private readonly Brush bottom;

        public MainWindow()
        {
            InitializeComponent();

            Initialize();

            YouTubeClient client = new();
            DataService data = new();
            musicService = new(data, client);

            playlistManager = new(ref lvPlaylists, ref lvPlaylist, ref lvPlaying, data, musicService, client);
            playerService = new(ref lvPlaying, ref media, musicService, (a) => icon.ToolTipText = a);
            addonManager = new(ref lvLocations, ref lvFavourites, musicService);
            enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.OnVolumeNotification += async (a) => await Dispatcher.InvokeAsync(() =>
            {
                if (!GetBool("STM") || systemVolume != a.MasterVolume)
                {
                    systemVolume = a.MasterVolume;
                    return;
                }
                SetPlayState(!a.Muted);
                playerService.TogglePlay(!a.Muted);
            });
            update = new UpdateService();

            CheckDB(data);
            SwitchMenu(0);
            if (!configService.ConfigExists())
                configService.CreateFile();
            configService.FixFile();
            SetupArgs();

            var desc = new System.ComponentModel.SortDescription("Index", System.ComponentModel.ListSortDirection.Ascending);
            CollectionView viewPlaying = (CollectionView)CollectionViewSource.GetDefaultView(lvPlaying.ItemsSource);
            viewPlaying.SortDescriptions.Add(desc);
            CollectionView viewPlaylist = (CollectionView)CollectionViewSource.GetDefaultView(lvPlaylist.ItemsSource);
            viewPlaylist.SortDescriptions.Add(desc);

            Application.Current.DispatcherUnhandledException += (a, b) =>
            {
                if (DEBUG_MODE)
                    MessageBox.Show($"Hata: {b.Exception.Message}\nStack Trace: \n{b.Exception.StackTrace}");
                else
                    MessageBox.Show("Program işleyişinde bir hata oluştu. " + b.Exception.HResult, "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                b.Handled = true;
            };

            bottom = panelBottom.Background;
            RefreshTheme();

            Restore();

            interop = new InteropService(true);
            interop.PlayPausePress += async () => await Dispatcher.InvokeAsync(() => SetPlayState(playerService.TogglePlay()));

            if (GetBool("FIRST"))
            {
                addonManager.AddLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Music").GetAwaiter().GetResult();
                configService.Set("FIRST", "FALSE");
            }
            CheckUpdate();
        }

        #region Methods
        public void SetVolume(double nowValue, double volume)
        {
            this.volume = volume;
            sldVolume.Value = nowValue == 0 ? 0 : volume;
        }
        private async void CheckUpdate() =>
            await Dispatcher.InvokeAsync(async () =>
            {
                var isUp = await update.CheckUpdate(GetVersion());
                if (isUp.Item1)
                {
                    var result = MessageBox.Show("Uyarı", $"Yeni bir sürüm (v{isUp.Item2.ToString().Replace(',', '.')}) mevcut!\nHemen güncellemek ister misiniz?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                        new DownloadBox(isUp.Item3, System.IO.Path.Combine(Environment.CurrentDirectory, "New.zip")).ShowDialog();
                }
            });
        public static string GetVersion()
        {
            Version version = Application.ResourceAssembly.GetName().Version;
            return $"{version.Major}.{version.Minor}";
        }
        private async void CheckDB(DataService service)
        {
            if (DataService.Test())
                return;
            await service.BuildDatabase();
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
        public void SetPlayState(bool playing)
        {
            if (!playing)
            {
                btnPlay.Content = "ᐅ";
                btnPlayContext.Header = "Oynat";
            }
            else
            {
                btnPlay.Content = "| |";
                btnPlayContext.Header = "Duraklat";
            }
        }
        public void SwitchVisibility()
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
                btnSHWindow.Header = "Göster";
            }
            else
            {
                Show();
                btnSHWindow.Header = "Gizle";
            }
        }
        private void SwitchMenu(int menu)
        {
            if (menu > menus.Length - 1)
                return;
            object c1 = FindName(menus[menu, 0].ToString());
            if (c1 is Button b1)
                b1.Background = new SolidColorBrush(Color.FromRgb(20, 40, 50));
            else
                ((Control)c1).Visibility = Visibility.Hidden;
            Control c2 = (Control)FindName(menus[menu, 1].ToString());
            c2.Visibility = Visibility.Visible;
            btnAdd.Visibility = (bool)menus[menu, 2] ? Visibility.Visible : Visibility.Hidden;
            btnPlayList.Visibility = (bool)menus[menu, 3] ? Visibility.Visible : Visibility.Hidden;
            for (int i = 0; i < menus.Length / 4; i++)
                if (i != menu)
                {
                    object f = FindName(menus[i, 0].ToString());
                    if (f is Button b2)
                        b2.Background = new SolidColorBrush(Colors.Transparent);
                    ((Control)FindName(menus[i, 1].ToString())).Visibility = Visibility.Hidden;
                }
            switchedMenu = menu;

            inpAddLocation.Visibility = Visibility.Hidden;
            inpPlaylistName.Visibility = Visibility.Hidden;
            inpPlaylistSearch.Visibility = menu == 2 ? Visibility.Visible : Visibility.Hidden;
            btnImport.Visibility = menu == 1 ? Visibility.Visible : Visibility.Hidden;
        }
        private void Shuffle()
        {
            if (!playerService.IsPlayingList)
                return;
            if (playerService.isShuffled)
            {
                playerService.Shuffle(false);
                btnShuffle.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                playerService.Shuffle(true);
                playerService.PlayFirst();
                btnShuffle.Foreground = new LinearGradientBrush(Colors.Red, Colors.Aqua, 45);
            }
        }
        private void Infinite()
        {
            if (!playerService.isLoop && !playerService.isListLoop)
            {
                playerService.isLoop = true;
                btnInfinite.Foreground = new SolidColorBrush(Colors.Aqua);
            }
            else if (!playerService.isListLoop)
            {
                playerService.isLoop = false;
                playerService.isListLoop = true;
                btnInfinite.Foreground = new LinearGradientBrush(Colors.Aqua, Colors.Red, 45);
            }
            else
            {
                playerService.isListLoop = false;
                playerService.isLoop = false;
                btnInfinite.Foreground = new SolidColorBrush(Colors.White);
            }
        }
        private void SetupArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            DEBUG_MODE = args.Contains("-debug") || args.Contains("-d");
        }
        public bool GetBool(string column) =>
            configService.Get(column).ToLower() == "true";
        public async void Restore() =>
            await playerService.RestoreState();
        public Color? GetColor(string column, bool d = false)
        {
            string[] spl = (d ? configService.GetDefault(column) : configService.Get(column)).Split(',');
            if (spl.Length != 3)
                return null;
            return Color.FromRgb(byte.Parse(spl[0]), byte.Parse(spl[1]), byte.Parse(spl[2]));
        }
        public static bool IsValidImage(string path)
        {
            if (!System.IO.File.Exists(path))
                return false;
            string extension = System.IO.Path.GetExtension(path);
            return extension is ".jpg" or ".png";
        }
        public void RefreshTheme()
        {
            string back = configService.Get("BACKGROUND");
            if (IsValidImage(back))
            {
                panelBottom.Background = new SolidColorBrush(Colors.Transparent);
                panelMain.Background = new ImageBrush(new BitmapImage(new Uri(back)));
            }
            else
            {
                var x = GetColor("BTOP");
                panelBottom.Background = x == null ? new SolidColorBrush(x.Value) : bottom;
                panelMain.Background = new SolidColorBrush(GetColor("BGENERAL") ?? Color.FromRgb(41, 41, 41));
            }
            double opacity = (double)int.Parse(configService.Get("BGOPACITY")) / 100;
            panelListing.Opacity = opacity;
            string plT = configService.Get("PLAYERTHEME");
            if (plT == "1")
            {
                panelPlayer.Background = new LinearGradientBrush(Color.FromRgb(10, 130, 84), Color.FromRgb(17, 49, 89), 90);
                sldVolume.Foreground = new SolidColorBrush(Color.FromRgb(13, 127, 100));
            }
            else if (plT == "2")
            {
                panelPlayer.Background = new LinearGradientBrush(Colors.Purple, Color.FromRgb(80, 44, 143), 90);
                sldVolume.Foreground = new SolidColorBrush(Color.FromRgb(126, 2, 129));
            }
            else if (plT == "3")
            {
                panelPlayer.Background = new LinearGradientBrush(Color.FromRgb(20, 20, 35), Color.FromRgb(40, 40, 45), 45);
                sldVolume.Foreground = new SolidColorBrush(Color.FromRgb(21, 21, 36));
            }
        }
        #endregion

        #region Events
        private async void LikeButton_Click(object sender, RoutedEventArgs e) =>
            await addonManager.AddFavourite(sender as Button);
        private async void DelFav_Click(object sender, RoutedEventArgs e) =>
            await addonManager.DeleteFavourite(sender as Button);
        private async void DelLocation_Click(object sender, RoutedEventArgs e) =>
                await addonManager.DeleteLocation(sender as Button);
        private void AddLocationPlaylist_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            switchedLocationIdentity = (btn.DataContext as dynamic).Item.Path;
            inpPlaylistName.Visibility = inpPlaylistName.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            txtNamePlaylist.Focus();
        }
        private void LikeButton_Enter(object sender, MouseEventArgs e)
        {
            Button btn = sender as Button;
            btn.Foreground = new SolidColorBrush(Colors.Aqua);
        }
        private void DelFav_Enter(object sender, MouseEventArgs e)
        {
            Button btn = sender as Button;
            btn.Foreground = new SolidColorBrush(Colors.Aqua);
        }
        private void LikeButton_Leave(object sender, MouseEventArgs e)
        {
            Button btn = sender as Button;
            if (!(bool)btn.Tag)
                btn.Foreground = new SolidColorBrush(Colors.White);
            else
                btn.Foreground = new SolidColorBrush(Colors.Aquamarine);
        }
        private void DelFav_Leave(object sender, MouseEventArgs e)
        {
            Button btn = sender as Button;
            btn.Foreground = new SolidColorBrush(Colors.White);
        }
        private async void LikeButton_Loaded(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            bool isFavourite = await musicService.IsFavourite(((dynamic)btn.DataContext).Item.Music.SaveIdentity);
            if (isFavourite)
                btn.Foreground = new SolidColorBrush(Colors.Aquamarine);
            btn.Tag = isFavourite;
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await playlistManager.LoadPlaylists();
            await addonManager.LoadFavourites();
            await addonManager.LoadLocations();
        }
        private async void PlayLocation_Click(object sender, RoutedEventArgs e)
        {
            await playerService.PlayLocation(sender as Button);
            SwitchMenu(0);
        }
        private async void DelPlaylist_Click(object sender, RoutedEventArgs e) =>
            await playlistManager.DeletePlaylistAsync(sender as Button);
        private async void EditPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (await playlistManager.LoadPlaylist(sender as Button))
                SwitchMenu(2);
            else
            {
                MessageBoxResult result = MessageBox.Show("Bu liste dinamik YouTube listesi olduğundan düzenlenemez. Liste statik listeye dönüştürülsün mü?", "Uyarı", MessageBoxButton.YesNo);
                if (result != MessageBoxResult.Yes)
                    return;
                await playlistManager.ConvertYouTubeListAsync(((dynamic)sender).DataContext.Item);
            }
        }
        private void ExportPlaylist_Click(object sender, RoutedEventArgs e)
        {
            dynamic context = (sender as Button).DataContext;
            playlistManager.ExportList(context.Item);
        }
        private async void DelMusic_Click(object sender, RoutedEventArgs e) =>
            await playlistManager.DeleteMusicAsync(sender as Button);
        private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
        {
            dynamic context = (sender as Button).DataContext;
            listIdToRename = context.Item.ID;
            inpPlaylistName.Visibility = inpPlaylistName.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            txtNamePlaylist.Text = context.Item.Name;
            txtNamePlaylist.Focus();
            txtNamePlaylist.SelectionStart = txtNamePlaylist.Text.Length;
        }
        #endregion

        #region Init
        private void Initialize()
        {
            sldVolume.ValueChanged += (a, b) =>
            {
                media.Volume = b.NewValue / 100;
                if (b.NewValue != 0 && b.NewValue != volume)
                    volume = b.NewValue;
            };
            sldPosition.ValueChanged += (a, b) => playerService.SetPosition(sldPosition.Value);

            btnExit.Click += (a, b) =>
            {
                interop.Stop();
                playerService.SaveState();
                icon.Dispose();
                Environment.Exit(0);
            };
            btnExitContext.Click += (a, b) =>
            {
                interop.Stop();
                playerService.SaveState();
                icon.Dispose();
                Environment.Exit(0);
            };
            PreviewKeyDown += (a, b) =>
            {
                if (b.OriginalSource is TextBox)
                    return;
                if (b.Key is Key.Space)
                    SetPlayState(playerService.TogglePlay());
                else if (b.Key is Key.MediaPreviousTrack)
                    playerService.Previous();
                else if (b.Key is Key.MediaNextTrack)
                    playerService.Next();
                else if (b.Key is Key.Up)
                    sldVolume.Value += 5;
                else if (b.Key is Key.Left)
                    sldPosition.Value -= 5;
                else if (b.Key is Key.Right)
                    sldPosition.Value += 5;
                else if (Keyboard.Modifiers is ModifierKeys.Shift && b.Key is Key.Down)
                    SwitchVisibility();
                else if (b.Key == Key.Down)
                    sldVolume.Value -= 5;
                else if (b.Key is Key.MediaStop || (Keyboard.Modifiers is ModifierKeys.Shift && b.Key is Key.Delete))
                    playerService.Stop();
                else if (Keyboard.Modifiers is ModifierKeys.Shift && b.Key is Key.R)
                    Shuffle();
                else if (b.Key is Key.R)
                    Infinite();
                else if (b.Key is Key.M)
                    sldVolume.Value = sldVolume.Value == 0 ? volume : 0;
            };
            btnMaximize.Click += (a, b) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            btnMinimize.Click += (a, b) => WindowState = WindowState.Minimized;
            
            btnInfo.Click += (a, b) => new AlertBox("Hakkında", $"Ürün: Cynth Müzik\nSürüm: {GetVersion()}\nYapımcı: Furkan M Yılmaz / Corelium INC").ShowDialog();
            btnPlay.Click += (a, b) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    playerService.Stop();
                else
                    SetPlayState(playerService.TogglePlay());
            };
            btnStop.Click += (a, b) => playerService.Stop();
            btnPlayContext.Click += (a, b) => SetPlayState(playerService.TogglePlay());
            btnNext.Click += (a, b) => playerService.Next();
            btnNextContext.Click += (a, b) => playerService.Next();
            btnPrevious.Click += (a, b) => playerService.Previous();
            btnPreviousContext.Click += (a, b) => playerService.Previous();
            btnListNow.Click += (a, b) => SwitchMenu(0);
            btnListLists.Click += (a, b) => SwitchMenu(1);
            btnListFavourites.Click += (a, b) => SwitchMenu(3);
            btnExplore.Click += (a, b) => SwitchMenu(4);
            btnInfinite.Click += (a, b) => Infinite();
            btnShuffle.Click += (a, b) => Shuffle();
            btnMinimize.Click += (a, b) =>
            {
                if (Keyboard.IsKeyDown(Key.LeftShift))
                    SwitchVisibility();
            };
            Closing += (a, b) =>
            {
                playerService.SaveState();
                icon.Dispose();
            };
            btnSHWindow.Click += (a, b) => SwitchVisibility();
            txtAddLocation.KeyDown += async (a, b) =>
            {
                if (b.Key != Key.Enter)
                    return;
                bool isSuccess = await addonManager.AddLocation(txtAddLocation.Text);
                if (!isSuccess)
                    new AlertBox("Hata", "Lütfen geçerli bir konum girin. Konum daha önceden var olabilir veya geçersiz olabilir.").ShowDialog();
                else
                {
                    inpAddLocation.Visibility = Visibility.Hidden;
                    txtAddLocation.Clear();
                    txtAddLocation.Focus();
                    txtAddLocation.SelectionStart = 0;
                }
            };
            txtNamePlaylist.KeyDown += async (a, b) =>
            {
                if (b.Key != Key.Enter)
                    return;
                bool isSuccess = listIdToRename != -1 ? 
                await playlistManager.RenamePlaylistAsync(listIdToRename, txtNamePlaylist.Text) :
                await addonManager.AddPlaylistFromLocation(switchedLocationIdentity, txtNamePlaylist.Text);
                if (!isSuccess)
                    new AlertBox("Hata", "Lütfen geçerli bir liste ismi girin, boşluk veya önceden var olan bir listenin ismini koyamazsınız. Seçtiğiniz konumda müzik de olmayabilir.").ShowDialog();
                else
                {
                    listIdToRename = -1;
                    txtNamePlaylist.Clear();
                    SwitchMenu(1);
                }
            };
            icon.TrayLeftMouseUp += (a, b) => Show();
            lblState.MouseDoubleClick += (a, b) =>
            {
                int? id = playerService.GetPlayingID();
                if (playerService.IsPlayingList && id.HasValue)
                {
                    lvPlaying.SelectedIndex = id.Value - 1;
                    lvPlaying.ScrollIntoView(lvPlaying.Items[id.Value - 1]);
                }
            };
            btnAdd.Click += async (a, b) =>
            {
                if (switchedMenu == 1)
                    new Views.ListBox().ShowDialog();
                else if (switchedMenu == 2)
                    new MusicBox(await playerService.GetLoadedList()).ShowDialog();
                else if (switchedMenu == 4)
                {
                    inpAddLocation.Visibility = inpAddLocation.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
                    txtAddLocation.Focus();
                }
            };
            btnPlayList.Click += async (a, b) =>
            {
                int menu = switchedMenu;
                SwitchMenu(0);
                if (menu == 2)
                    await playerService.PlayLoadedList();
                else if (menu == 3 && lvFavourites.Items.Count > 0)
                    await playerService.PlayFavourites();
            };
            lvPlaylist.PreviewMouseLeftButtonDown += (a, b) =>
            {
                int index = lvPlaylist.SelectedIndex;
                if (index != -1)
                {
                    var x = lvPlaylist.Items.GetItemAt(index);
                    DragDrop.DoDragDrop(a as ListView, x, DragDropEffects.Move);
                }
            };
            lvPlaylist.AllowDrop = true;
            lvPlaylist.Drop += async (a, b) =>
            {
                Type type = typeof(Orderable<IMusic>);
                if (!b.Data.GetDataPresent(type))
                    return;
                var data = (Orderable<IMusic>)b.Data.GetData(type);
                dynamic targetItem = lvPlaylist.InputHitTest(b.GetPosition(lvPlaylist));
                if ((targetItem.DataContext as Orderable<IMusic>?) == null)
                    return;
                await playlistManager.ExchangeOrders(data.Index - 1, ((Orderable<IMusic>)targetItem.DataContext).Index - 1);
            };
            lvPlaying.MouseDoubleClick += async (a, b) =>
            {
                if (lvPlaying.SelectedIndex == -1)
                    return;
                dynamic selected = lvPlaying.SelectedItems[0];
                if (selected.Item.Music.PlayURL == null)
                    await playerService.PlayMusicWithLoad(selected);
                else
                    playerService.Play(selected.Item.Music.SaveIdentity);
            };
            lvPlaying.KeyDown += (a, b) =>
            {
                if (b.Key == Key.Escape)
                    lvPlaying.SelectedIndex = -1;
            };
            lvPlaylists.MouseDoubleClick += async (a, b) =>
            {
                if (lvPlaylists.SelectedIndex == -1)
                    return;
                SwitchMenu(0);

                IMusicList list = playlistManager.GetMusicList(lvPlaylists.SelectedIndex);
                if (list is YouTubeMusicList list1)
                    list = await playlistManager.GetYouTubeListAsync(list1.Location, playlistManager.GetAlgCheck(), playlistManager.GetAlgAuthors());

                await playerService.PlayMusicList(list);
            };
            btnSelectLocation.Click += (a, b) =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK)
                    return;
                txtAddLocation.Text = dialog.SelectedPath.Replace("\\", @"\\");
            };
            inpPlaylistSearch.TextChanged += (a, b) =>
            {
                string text = inpPlaylistSearch.Text;
                var filter = playlistManager.Filter(text.ToLower());
                lvPlaylist.ItemsSource = filter;
            };
            btnSettings.Click += (a, b) => new Settings().ShowDialog();
            btnImport.Click += async (a, b) =>
            {
                bool state = await playlistManager.ImportList();
                if (!state)
                    new AlertBox("Hata", "Dosya geçersiz.");
                else
                    await lvPlaylists.Dispatcher.Invoke(async () => await playlistManager.LoadPlaylists());
            };
        }
        #endregion
    }
}
