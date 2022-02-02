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
using System.Windows.Interop;

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
            { "BGENERAL", "41,41,41" },
            { "BGSLIDER", "0,163,181" },
            { "BGOPACITY", "100" },
            { "BACKGROUND", "" },
            { "PLAYERTHEME", "1" },
            { "DEBUG", "FALSE" },
            { "VOL", "50,50" },
            { "PLINTRAY", "FALSE", "Program açıldığında oynatmak için tepsiye küçült" },
            { "FAUTH", "FALSE", "Şarkı isimlerinden sanatçıyı otomatik bul" },
            { "STM", "TRUE", "Ses kısıldığında şarkıyı otomatik durdur" },
            { "FIRST", "TRUE" },
            { "LANG", "TR" }
        };
        public static readonly LangService translator = new();

        private readonly MusicService musicService;
        public readonly LogService logger;

        private readonly object[,] menus = new object[5, 4]
        {
            { "btnListNow", "scrPlaying", false, false }, { "btnListLists", "scrPlaylists", true, false }, { "scrPlaylists", "scrPlaylist", true, true }, { "btnListFavourites", "scrFavourites", true, true }, { "btnExplore", "scrLocations", true, false }
        };

        private int switchedMenu;
        private string switchedLocationIdentity;
        private int listIdToRename = -1;
        public double volume = 50;
        private float systemVolume = 0;
        private bool openMute = true;

        private readonly MMDeviceEnumerator enumerator;
        private readonly MMDevice device;
        private InteropService interop;
        private readonly UpdateService update;

        private readonly Brush bottom;
        public readonly Brush defImg;

        public MainWindow()
        {
            InitializeComponent();
            defImg = imgMusic.Fill;

            SetupLang();

            logger = new(System.IO.Path.Combine(Environment.CurrentDirectory, "Logs"));

            InitLog("Starting initialization...");

            if (System.Diagnostics.Process.GetProcessesByName("CynthMusic").Length > 1)
            {
                bool? result = new AlertBox("Uyarı", "Zaten çalışan bir Cynth uygulaması var, veri kaybını önlemek için çalışan örneği kapatıp yeniden deneyin. Devam etmeniz önerilmez, devam etmek istiyor musunuz?", true).ShowDialog();
                if (result == null || !result.Value)
                    Environment.Exit(0);
            }

            Initialize();
            YouTubeClient client = new();
            DataService data = new();
            musicService = new(data, client);

            playlistManager = new(ref lvPlaylists, ref lvPlaylist, ref lvPlaying, data, musicService, client);
            playerService = new(ref lvPlaying, ref media, musicService);
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
                if (a.Muted)
                {
                    if (GetMediaState() != MediaState.Pause)
                    {
                        playerService.Pause();
                        openMute = true;
                    }
                    else
                        openMute = false;
                }
                else if (!a.Muted && playerService.playingMusic?.ID != null && openMute)
                    playerService.Resume();
            });
            update = new UpdateService();
            InitLog("1-) Generation completed.");

            CheckDB(data);
            SwitchMenu(0);
            if (!configService.ConfigExists())
                configService.CreateFile();
            configService.FixFile();
            SetupArgs();

            InitLog("2-) Check-up completed.");

            var desc = new System.ComponentModel.SortDescription("Index", System.ComponentModel.ListSortDirection.Ascending);
            CollectionView viewPlaying = (CollectionView)CollectionViewSource.GetDefaultView(lvPlaying.ItemsSource);
            viewPlaying.SortDescriptions.Add(desc);
            CollectionView viewPlaylist = (CollectionView)CollectionViewSource.GetDefaultView(lvPlaylist.ItemsSource);
            viewPlaylist.SortDescriptions.Add(desc);

            Application.Current.DispatcherUnhandledException += (a, b) =>
            {
                if (DEBUG_MODE)
                    MessageBox.Show($"{translator.Get("error")}: {b.Exception.Message}\nStack Trace: \n{b.Exception.StackTrace}");
                else
                    new AlertBox(translator.Get("critical"), $"{translator.Get("msgError")} {b.Exception.HelpLink}").ShowDialog();
                logger.Log(LogType.ERROR, b.Exception.Message, b.Exception.StackTrace);
                b.Handled = true;
            };

            bottom = panelBottom.Background;
            RefreshTheme();

            var b = (System.Windows.Media.Animation.Storyboard)Resources["ImgBoard"];
            b.Begin();
            b.Pause();

            if (GetBool("FIRST"))
            {
                addonManager.AddLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Music").GetAwaiter().GetResult();
                configService.Set("FIRST", "FALSE");
            }

            InitLog("3-) Checking updates...");
            CheckUpdate();
            InitLog("Initialization finish.");
        }

        #region Methods
        private void InitLog(string content) =>
            logger.Log(LogType.INFO, "Setup", content);
        private void Rename(int id, string def)
        {
            listIdToRename = id;
            inpPlaylistName.Visibility = inpPlaylistName.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            txtNamePlaylist.Text = def;
            txtNamePlaylist.Focus();
            txtNamePlaylist.SelectionStart = txtNamePlaylist.Text.Length;
        }
        public void SetVolume(double nowValue, double volume)
        {
            this.volume = volume;
            sldVolume.Value = nowValue == 0 ? 0 : volume;
        }
        private MediaState GetMediaState()
        {
            FieldInfo hlp = typeof(MediaElement).GetField("_helper", BindingFlags.NonPublic | BindingFlags.Instance);
            object helperObject = hlp.GetValue(media);
            FieldInfo stateField = helperObject.GetType().GetField("_currentState", BindingFlags.NonPublic | BindingFlags.Instance);
            MediaState state = (MediaState)stateField.GetValue(helperObject);
            return state;
        }
        private async void CheckUpdate() =>
            await Dispatcher.InvokeAsync(async () =>
            {
                var isUp = await update.CheckUpdate(GetVersion());
                if (isUp.Item1)
                {
                    var result = MessageBox.Show($"Yeni bir sürüm (v{isUp.Item2.ToString().Replace(',', '.')}) mevcut!\nHemen güncellemek ister misiniz?", "Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                        new DownloadBox(isUp.Item3, System.IO.Path.Combine(Environment.CurrentDirectory, "New.zip")).ShowDialog();
                }
            });
        public static string GetVersion()
        {
            Version version = Application.ResourceAssembly.GetName().Version;
            return $"{version.Major}.{version.Minor}";
        }
        private async void CheckDB(DataService service) =>
            await service.BuildDatabase();
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
        
        public void SwitchVisibility()
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
                btnSHWindow.Foreground = new SolidColorBrush(Colors.Aqua);
            }
            else
            {
                Show();
                btnSHWindow.Foreground = new SolidColorBrush(Colors.White);
            }
        }
        private void SwitchMenu(int menu)
        {
            if (menu > menus.Length - 1)
                return;
            object c1 = FindName(menus[menu, 0].ToString());
            if (c1 is Button b1)
                b1.FontWeight = FontWeights.SemiBold;
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
                        b2.FontWeight = FontWeights.Normal;
                    ((Control)FindName(menus[i, 1].ToString())).Visibility = Visibility.Hidden;
                }
            switchedMenu = menu;

            inpAddLocation.Visibility = Visibility.Hidden;
            inpPlaylistName.Visibility = Visibility.Hidden;
            inpPlaylistSearch.Visibility = menu == 2 || menu == 0 ? Visibility.Visible : Visibility.Hidden;
            btnImport.Visibility = menu == 1 ? Visibility.Visible : Visibility.Hidden;
        }
        private async void Shuffle()
        {
            if (!playerService.isLoaded)
                return;
            if (playerService.isShuffled)
            {
                playerService.Shuffle(false);
                btnShuffle.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                playerService.Shuffle(true);
                await playerService.PlayMusic(playerService.GetShuffledList().FirstOrDefault());
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
            string extension = System.IO.Path.GetExtension(path).ToLower();
            return extension is ".jpg" or ".png" or ".jpeg";
        }
        public void RefreshTheme()
        {
            string back = configService.Get("BACKGROUND");
            if (IsValidImage(back))
                panelMain.Background = new ImageBrush(new BitmapImage(new Uri(back)));
            else
                panelMain.Background = new SolidColorBrush(GetColor("BGENERAL") ?? Color.FromRgb(41, 41, 41));
            scrFavourites.Background = scrLocations.Background = scrPlaying.Background = scrPlaylist.Background = scrPlaylists.Background =
                new SolidColorBrush(GetColor("BGSLIDER") ?? Color.FromRgb(0, 163, 181));
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
        public void SetupLang()
        {
            translator.Language = configService.Get("LANG");
            translator.FromJS(Application.ResourceAssembly.GetManifestResourceStream("CynthMusic.language.json"));
            
            btnListNow.Content = translator.Get("menuNow");
            btnListLists.Content = translator.Get("menuMyLists");
            btnListFavourites.Content = translator.Get("menuFavs");
            btnExplore.Content = translator.Get("menuExplore");
            btnSettings.Content = translator.Get("settings");

            lblEnterName.Content = translator.Get("enterName");
            lblEnterLocation.Content = translator.Get("enterLocation");

            grdPlaying.Columns[1].Header = translator.Get("name");
            grdPlaying.Columns[2].Header = translator.Get("author");
            grdPlaying.Columns[3].Header = translator.Get("length");

            grdLists.Columns[0].Header = translator.Get("name");
            grdLists.Columns[1].Header = translator.Get("creator");
            grdLists.Columns[2].Header = translator.Get("total");

            grdFavs.Columns[0].Header = translator.Get("name");
            grdFavs.Columns[1].Header = translator.Get("author");

            grdLocations.Columns[1].Header = translator.Get("location");

            grdList.Columns[1].Header = translator.Get("name");
            grdList.Columns[2].Header = translator.Get("author");
            grdList.Columns[3].Header = translator.Get("length");

            configService.SetPreview("PLINTRAY", translator.Get("setTray"));
            configService.SetPreview("FAUTH", translator.Get("setAutoAuthor"));
            configService.SetPreview("STM", translator.Get("setMuteStop"));

            if (!(playerService?.isLoaded ?? false))
                lblState.Content = translator.Get("idle");
        }
        #endregion

        #region Events
        private async void Exit(object s, RoutedEventArgs e)
        {
            interop?.Stop();
            if (playerService is not null)
                await playerService.SaveState();
            icon?.Dispose();
            Environment.Exit(0);
        }
        private async void LikeButton_Click(object sender, RoutedEventArgs e) =>
            await addonManager.AddFavourite(sender as Button);
        private async void DelFav_Click(object sender, RoutedEventArgs e) =>
            await addonManager.DeleteFavourite((sender as dynamic).DataContext.Item);
        private async void DelLocation_Click(object sender, RoutedEventArgs e) =>
                await addonManager.DeleteLocation(sender as Button);
        private void AddLocationPlaylist_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            switchedLocationIdentity = (btn.DataContext as dynamic).Item.Path;
            inpPlaylistName.Visibility = inpPlaylistName.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            txtNamePlaylist.Focus();
        }
        private async void TemporaryPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lvPlaying.SelectedItems.Count == 0)
                return;
            await playerService.PlayTemp((Orderable<ColorableMusic>)lvPlaying.SelectedItem);
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
            var handle = new WindowInteropHelper(this).Handle;
            interop = new InteropService(handle);
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(interop.WndProc);

            interop.PlayPausePress += async () => await Dispatcher.InvokeAsync(() => playerService.Switch());
            interop.MediaNextPress += async () => await Dispatcher.InvokeAsync(() => playerService.Next());
            interop.MediaPreviousPress += async () => await Dispatcher.InvokeAsync(() => playerService.Previous());
            interop.MediaRefreshPress += async () => await Dispatcher.InvokeAsync(() => media.Position = TimeSpan.Zero);

            await playlistManager.LoadPlaylists();
            await addonManager.LoadFavourites();
            await addonManager.LoadLocations();

            await playerService.RestoreState();
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
                bool? result = new AlertBox(translator.Get("warning"), translator.Get("msgDynamic"), true).ShowDialog();
                if (result != null && result.Value)
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
            Rename((int)context.Item.ID, (string)context.Item.Name);
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
            sldPosition.ValueChanged += (a, b) => media.Position = TimeSpan.FromSeconds(b.NewValue);

            PreviewKeyDown += async (a, b) =>
            {
                if (b.OriginalSource is TextBox)
                    return;
                if (b.Key is Key.Space)
                    playerService.Switch();
                else if (b.Key is Key.MediaPreviousTrack)
                    await playerService.Previous();
                else if (b.Key is Key.MediaNextTrack)
                    await playerService.Next();
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
                else if (b.Key is Key.R && Keyboard.Modifiers != ModifierKeys.Control)
                    Infinite();
                else if (b.Key is Key.R && Keyboard.Modifiers == ModifierKeys.Control && playerService.playingMusic?.ID != null)
                    await playerService.PlayMusic(playerService.srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == playerService.playingMusic.SaveIdentity), forceRefresh: true);
                else if (b.Key is Key.M)
                    sldVolume.Value = sldVolume.Value == 0 ? volume : 0;
            };
            btnMaximize.Click += (a, b) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            btnMinimize.Click += (a, b) => WindowState = WindowState.Minimized;
            btnPlay.Click += (a, b) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    playerService.Stop();
                else
                    playerService.Switch();
            };
            btnPlayContext.Click += (a, b) => playerService.Switch();
            btnNext.Click += async (a, b) => await playerService.Next();
            btnNextContext.Click += async (a, b) => await playerService.Next();
            btnPrevious.Click += async (a, b) => await playerService.Previous();
            btnPreviousContext.Click += async (a, b) => await playerService.Previous();
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
            Closing += async (a, b) =>
            {
                await playerService.SaveState();
                icon.Dispose();
            };
            btnSHWindow.Click += (a, b) => SwitchVisibility();
            txtAddLocation.KeyDown += async (a, b) =>
            {
                if (b.Key != Key.Enter)
                    return;
                bool isSuccess = await addonManager.AddLocation(txtAddLocation.Text);
                if (!isSuccess)
                    new AlertBox(translator.Get("error"), translator.Get("invalidPath")).ShowDialog();
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
                    new AlertBox(translator.Get("error"), translator.Get("invalidName")).ShowDialog();
                else
                {
                    listIdToRename = -1;
                    txtNamePlaylist.Clear();
                    SwitchMenu(1);
                }
            };
            lblState.MouseDoubleClick += (a, b) =>
            {
                int? id = playerService.playingMusic?.ID;
                if (playerService.isLoaded && id.HasValue)
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
                else if (switchedMenu == 3)
                {
                    var x = await addonManager.ConvertFavouritesToPlaylist();
                    Rename(x.Item1, x.Item2);
                }
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
                    await playerService.PlayMusicList(await playerService.GetLoadedList());
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
                await playerService.PlayMusic(selected);
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
                string text = inpPlaylistSearch.Text.ToLower();
                if (switchedMenu == 0)
                {
                    var filter = playerService.Filter(text);
                    lvPlaying.ItemsSource = filter;
                }
                else if (switchedMenu == 2)
                {
                    var filter = playlistManager.Filter(text);
                    lvPlaylist.ItemsSource = filter;
                }
            };
            btnSettings.Click += (a, b) => new Settings().ShowDialog();
            btnImport.Click += async (a, b) =>
            {
                bool state = await playlistManager.ImportList();
                if (!state)
                    new AlertBox(translator.Get("error"), translator.Get("invalidFile"));
                else
                    await lvPlaylists.Dispatcher.Invoke(async () => await playlistManager.LoadPlaylists());
            };
        }
        #endregion
    }
}
