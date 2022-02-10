/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore;
using CynthCore.Entities;
using CynthMusic.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using CynthMusic.Views;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace CynthMusic
{
    public class PlayerService : PlayerManager
    {
        private ListView lvPlaying;
        private MusicService musicService;
        private PlaylistManager playlistManager => MainWindow.playlistManager;
        private MainWindow window => (MainWindow)App.Current.MainWindow;
        private ConfigService config => MainWindow.configService;
        private AddonManager addon => MainWindow.addonManager;

        private CancellationTokenSource cancellation;

        public static int PlayingListID = -1;

        private List<int> toDeleteList;
        private string playingListYT;

        private IMusic oMusic;
        private TimeSpan oPos;
        private bool isTemp = false;

        public PlayerService(ref ListView lvPlaying, ref MediaElement player, MusicService musicService) :
            base(ref player)
        {
            this.musicService = musicService;
            this.lvPlaying = lvPlaying;

            cancellation = new CancellationTokenSource();
            srcPlaying = new OrderableCollection<ColorableMusic>();
            toDeleteList = new List<int>();
            lvPlaying.ItemsSource = srcPlaying;

            PositionChanged += (pos, duration, buffering) =>
            {
                if (buffering is not 0 and not 1)
                {
                    window.sldPosition.Foreground = new SolidColorBrush(Color.FromRgb(200, 170, 20));
                    window.sldPosition.Maximum = 100;
                    var progress = buffering * 100;
                    window.sldPosition.Value = progress;
                    window.lblPosition.Content = progress + " / 100";
                }
                else
                {
                    window.sldPosition.Foreground = new SolidColorBrush(Color.FromRgb(13, 127, 100));
                    if (!duration.HasTimeSpan)
                    {
                        window.sldPosition.Maximum = 0;
                        return;
                    }
                    window.sldPosition.Maximum = duration.TimeSpan.TotalSeconds;
                    window.sldPosition.Value = pos.TotalSeconds;
                    window.lblPosition.Content = pos.ToString(@"mm\.ss") + " / " + duration.TimeSpan.ToString(@"mm\.ss");
                }
            };

            MediaChanged += (a, b) =>
            {
                window.lblState.Content = window.lblStateContext.Content = a.Name;
                if (b)
                    SetPlayState(true);
            };
            MediaEnd += async (a) =>
            {
                if (!isLoop)
                {
                    if (isTemp)
                    {
                        await RestoreTemp();
                        return;
                    }
                    if (a == srcPlaying.Count - 1)
                    {
                        if (isListLoop)
                            await PlayMusic(srcPlaying.FirstOrDefault());
                        else
                            Stop();
                    }
                    else
                        await Next(a);
                }
                else
                    await PlayMusic(srcPlaying[playingMusic.ID]);
            };
            MediaFail += async (a) => await RetryMedia();
        }
        private async Task RetryMedia()
        {
            if (retry == 0)
            {
                retry = 5;
                if (isLoaded)
                    await Next();
                else
                    Stop();
                return;
            }
            await PlayMusic(srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == playingMusic.SaveIdentity), forceRefresh: true);
            retry--;
        }
        public void CancelToken()
        {
            cancellation.Cancel();
            cancellation = new CancellationTokenSource();
        }
        public CancellationToken GetToken() =>
            cancellation.Token;
        public async Task<IMusicList> GetLoadedList() =>
            await playlistManager.GetAsync(PlaylistManager.loadedPlaylistId);

        public IEnumerable<Orderable<ColorableMusic>> Filter(string q) =>
            string.IsNullOrWhiteSpace(q) ? srcPlaying :
            srcPlaying.Where(x =>
                x.Item.Music.Name.ToLower().Contains(q) ||
                (x.Item.Music.Author != null && x.Item.Music.Author.ToLower().Contains(q)));

        #region Play
        private async Task PlayNormalMusicList(MusicList list, bool play, IEnumerable<string> shuff = null)
        {
            if (!list.Verify())
            {
                await App.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var musics = list.Musics.Where(x => x != null);
                    list.Musics = musics;
                    await playlistManager.UpdateMusicsAsync(list.ID, list.Musics);
                    await PlayNormalMusicList(list, play);
                });
                return;
            }
            foreach (var m in list.Musics)
            {
                m.IsFavourite = await musicService.IsFavourite(m.SaveIdentity);
                srcPlaying.Add(new ColorableMusic(m));
            }
            ShuffSet(shuff);
            if (srcPlaying.Count != 0 && play)
                await PlayMusic(srcPlaying.FirstOrDefault());
            
        }
        private async Task PlayYouTubeMusicList(YouTubeMusicList list, CancellationToken token, bool play, IEnumerable<string> shuff) =>
            await lvPlaying.Dispatcher.InvokeAsync(async () =>
            {
                playingListYT = list.YouTubeID;
                foreach (var x in list.Musics)
                {
                    if (token.IsCancellationRequested)
                        return;
                    x.IsFavourite = await musicService.IsFavourite(x.SaveIdentity);
                    srcPlaying.Add(new ColorableMusic(x));
                    if (x.ID == 1 && play)
                        await PlayMusic(srcPlaying.FirstOrDefault());
                }
                ShuffSet(shuff);
            });
        public async Task PlayMusicList(IMusicList list, bool play = true, bool save = true, IEnumerable<string> shuff = null)
        {
            Stop();

            PlayingListID = list.ID == -1 ? -2 : list.ID;
            if (save)
                await SaveState();
            if (list is YouTubeMusicList l)
                await PlayYouTubeMusicList(l, GetToken(), play, shuff);
            else
                await PlayNormalMusicList((MusicList)list, play, shuff);
        }
        public async Task PlayMusic(Orderable<ColorableMusic> music, TimeSpan? position = null, bool autoPlay = true, bool forceRefresh = false) =>
            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (music.Item.Music is null)
                    return;

                if (music.Item.Music is Music mm)
                {
                    byte[] buff = mm.Thumbnail?.Data.Data;
                    if (buff != null)
                    {
                        var img = new BitmapImage();
                        using (var stream = new System.IO.MemoryStream(buff))
                        {
                            stream.Position = 0;
                            img.BeginInit();
                            img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.StreamSource = stream;
                            img.EndInit();
                        }
                        img.Freeze();
                        window.imgMusic.Fill = new ImageBrush(img);
                    }
                    
                    Play(music.Item.Music, position, autoPlay);
                    return;
                }
                YouTubeMusic? m;
                if (music.Item.Music.PlayURL == null || forceRefresh)
                {
                    m = await musicService.GetConvertedYouTubeMusicAsync(((YouTubeMusic)music.Item.Music).YouTubeUri);
                    if (m == null)
                    {
                        window.Dispatcher.Invoke(() =>
                        {
                            string msg = ExceptionManager.SolveHttp(ExceptionManager.GetExceptions("getMusicWithStream").LastOrDefault());
                            new AlertBox(MainWindow.translator.Get("error"), msg + "\n\nVideo: " + music.Item.Music.Name).Show();
                        });
                        await Next();
                        return;
                    }
                }
                else
                    m = (YouTubeMusic)music.Item.Music;
                var item = srcPlaying[music.Index - 1];
                item.Item.Music.Author = m.Value.Author;
                item.Item.Music.PlayURL = m.Value.PlayURL;
                item.Item.Music.Length = m.Value.Length;
                srcPlaying.SetItem(music.Index - 1, item);
                lvPlaying.Items.Refresh();
                window.imgMusic.Fill = ((YouTubeMusic)music.Item.Music).Thumbnail != null ? new ImageBrush(new BitmapImage(new Uri(m.Value.Thumbnail))) : window.defImg;
                Play(music.Item.Music, position, autoPlay);
            });
        public async Task PlayLocation(Button btn)
        {
            var item = (Orderable<Location>)btn.DataContext;
            var list = playlistManager.GetFromPath(item.Item.Path, false).ElementAt(0);
            if (list.Musics.Count() == 0)
                return;
            await PlayMusicList(list);
        }
        public async Task PlayFavourites()
        {
            Stop();
            srcPlaying.Clear();
            foreach (var x in addon.GetAllFavourites())
                srcPlaying.Add(new ColorableMusic(x));
            await PlayMusic(srcPlaying.FirstOrDefault());
        }
        public new void Stop()
        {
            base.Stop();
            SetPlayState(false);
            window.imgMusic.Fill = window.defImg;
            window.sldPosition.Value = 0;
            window.lblState.Content = window.lblStateContext.Content = MainWindow.translator.Get("idle");
            window.lblPosition.Content = "0.00 / 0.00";
            window.btnShuffle.Foreground = new SolidColorBrush(Colors.White);
            PlayingListID = -1;
            srcPlaying.Clear();
            CancelToken();
        }
        public void SetPlayState(bool playing)
        {
            if (!playing)
            {
                window.btnPlay.Content = "ᐅ";
                window.btnPlayContext.Content = "ᐅ";
                window.btnPlay.FontSize = 30;
                ((Storyboard)window.Resources["ImgBoard"]).Pause();
            }
            else
            {
                window.btnPlay.FontSize = 40;
                window.btnPlay.Content = "∣∣";
                window.btnPlayContext.Content = "∣∣";
                ((Storyboard)window.Resources["ImgBoard"]).Resume();
            }
        }
        public new void Pause()
        {
            base.Pause();
            SetPlayState(false);
        }
        public new void Resume()
        {
            base.Resume();
            SetPlayState(true);
        }
        public void Switch()
        {
            if (isPlaying)
                Pause();
            else
                Resume();
        }
        public new async Task Next(int? nIndex = null)
        {
            if (!isLoaded)
                return;
            if (isTemp)
                await RestoreTemp();
            else
                await PlayMusic(base.Next(nIndex));
        }
        public async Task Previous()
        {
            if (!isLoaded)
                return;
            if (isTemp)
                await RestoreTemp();
            else
                await PlayMusic(base.Previous());
        }
        public async Task PlayTemp(Orderable<ColorableMusic> music)
        {
            if (!isTemp)
            {
                oMusic = playingMusic;
                oPos = window.media.Position;
            }

            await PlayMusic(music);
            isTemp = true;
        }
        private async Task RestoreTemp()
        {
            await PlayMusic(srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == oMusic.SaveIdentity), oPos);
            oMusic = null;
            isTemp = false;
        }
        #endregion

        #region Utils
        public async Task RefreshPlaying() => await lvPlaying.Dispatcher.InvokeAsync(() => lvPlaying.Items.Refresh());
        public async Task RemoveMusic(Orderable<IMusic> m)
        {
            var item = srcPlaying[m.Index - 1];
            srcPlaying.DeleteItem(m.Index - 1);
            if (isShuffled)
                shuffler.Remove(item);
            if (playingMusic.SaveIdentity == m.Item.SaveIdentity)
                await Next();
        }
        public void AddMusic(IMusic m)
        {
            srcPlaying.Add(new ColorableMusic(m));
            if (isShuffled)
                shuffler.Add(srcPlaying.Last());
        }
        public async Task SaveState()
        {
            string x = ((int)window.volume) + "," + (int)window.sldVolume.Value;
            config.Set("VOL", x);

            await playlistManager.ClearSavedMusicsAsync();
            if (isLoaded)
            {
                string listId = PlayingListID == -2 ? "---" + playingListYT : PlayingListID.ToString();
                int playing = srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == playingMusic?.SaveIdentity).Index - 1;

                config.Set("LASTID", $"{listId}||{playing}||{Position}");
                if (isShuffled)
                    await playlistManager.SaveMusicsAsync(shuffler.Select(x => x.Item.Music.SaveIdentity));
            }
            else
                config.Set("LASTID", null);
        }
        public async Task RestoreState() =>
            await Task.Run(async () =>
            {
                string[] state = config.Get("LASTID").Split("||");
                var saved = await playlistManager.GetSavedMusicsAsync();
                if (state.Length != 3)
                    return;

                bool tray = config.Get("PLINTRAY").ToLower() == "true";

                try
                {
                    IMusicList list;
                    if (state[0].StartsWith("---"))
                    {
                        list = await playlistManager.GetYouTubeListAsync(YouTubeMusicList.GetLocation(state[0][3..]), playlistManager.GetAlgCheck(), playlistManager.GetAlgAuthors());
                        if (list == null)
                            return;
                    }
                    else
                        list = await playlistManager.GetAsync(int.Parse(state[0]), playlistManager.GetAlgCheck(), playlistManager.GetAlgAuthors());

                    if (list == null)
                        return;

                    await lvPlaying.Dispatcher.InvokeAsync(async () =>
                        await PlayMusicList(list, false, false, saved));
                    int count = list.Musics.Count();
                    //while (srcPlaying.Count != count) ;

                    try
                    {
                        int number = int.Parse(state[1]);
                        await PlayMusic(
                            srcPlaying[number <= 1 ? 0 : number],
                            TimeSpan.ParseExact(state[2], @"hh\.mm\.ss", CultureInfo.CurrentCulture),
                            tray);
                    }
                    catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidOperationException)
                    {

                    }
                    finally
                    {
                        while (!isLoaded) ;
                    }
                }
                catch (Exception e)
                {
                    await App.Current.Dispatcher.InvokeAsync(() => window.logger.Log(LogType.ERROR, "Restore Error", e.Message + "\n" + e.StackTrace));
                }

                App.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        string[] vol = config.Get("VOL").Split(',');
                        window.SetVolume(int.Parse(vol[1]), int.Parse(vol[0]));

                        if (tray)
                            window.SwitchVisibility();
                    }
                    catch (Exception e)
                    {
                        window.logger.Log(LogType.ERROR, "Restore Error", e.Message);
                    }
                });
            });
        private bool CheckShuffleCorrection() =>
            srcPlaying.Count != shuffler.Count() || (!shuffler.Except(srcPlaying).Any() && !srcPlaying.Except(shuffler).Any());
        private void FixShuffle()
        {
            foreach (var x in shuffler.Except(srcPlaying).ToList())
                shuffler.Remove(x);
            foreach (var x in srcPlaying.Except(shuffler))
                shuffler.Add(x);
        }
        private void ShuffSet(IEnumerable<string> shuff)
        {
            ResetShuffler();
            if (shuff != null && shuff.Any())
            {
                shuffler.FromEnumerable(shuff.Select(x => srcPlaying.FirstOrDefault(y => y.Item.Music.SaveIdentity == x)));
                if (CheckShuffleCorrection())
                    FixShuffle();
                isShuffled = true;
                window.btnShuffle.Foreground = new LinearGradientBrush(Colors.Red, Colors.Aqua, 45);
            }
        }
        public new void Shuffle(bool reverse) =>
            base.Shuffle(reverse);
        public IEnumerable<Orderable<ColorableMusic>> GetShuffledList() =>
            shuffler;
        #endregion
    }
}
