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

        public PlayerService(ref ListView lvPlaying, ref MediaElement player, MusicService musicService) :
            base(ref player)
        {
            this.musicService = musicService;
            this.lvPlaying = lvPlaying;

            cancellation = new CancellationTokenSource();
            srcPlaying = new OrderableCollection<ColorableMusic>();
            toDeleteList = new List<int>();
            lvPlaying.ItemsSource = srcPlaying;


            PositionChanged += (a, duration, buffering) =>
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
                    window.sldPosition.Value = a.TotalSeconds;
                    window.lblPosition.Content = a.ToString(@"mm\.ss") + " / " + duration.TimeSpan.ToString(@"mm\.ss");
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
                if (isPlaying)
                    await Next();
                else
                    Stop();
            }
            await PlayMusic(srcPlaying.FirstOrDefault(x => x.Item.Music.SaveIdentity == playingMusic.SaveIdentity));
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
        private async Task PlayNormalMusicList(MusicList list, bool play, string shuff = null)
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
        private async Task PlayYouTubeMusicList(YouTubeMusicList list, CancellationToken token, bool play, string shuff) =>
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
        public async Task PlayMusicList(IMusicList list, bool play = true, string shuff = null)
        {
            srcPlaying.Clear();
            Stop();

            PlayingListID = list.ID == -1 ? -2 : list.ID;
            SaveState();
            if (list is YouTubeMusicList l)
                await PlayYouTubeMusicList(l, GetToken(), play, shuff);
            else
                await PlayNormalMusicList((MusicList)list, play, shuff);
        }
        private void ShuffSet(string shuff)
        {
            ResetShuffler();
            if (!string.IsNullOrWhiteSpace(shuff))
            {
                shuffler.RestoreShuffle(shuff);
                isShuffled = true;
                window.btnShuffle.Foreground = new LinearGradientBrush(Colors.Red, Colors.Aqua, 45);
            }
        }
        public async Task PlayMusic(Orderable<ColorableMusic> music, TimeSpan? position = null, bool autoPlay = true) =>
            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (music.Item.Music is not YouTubeMusic)
                {
                    Play(music.Item.Music, position, autoPlay);
                    return;
                }
                YouTubeMusic? m;
                if (music.Item.Music.PlayURL == null)
                {
                    m = await musicService.GetConvertedYouTubeMusicAsync(((YouTubeMusic)music.Item.Music).YouTubeUri);
                    if (m == null)
                    {
                        window.Dispatcher.Invoke(() =>
                        {
                            string msg = ExceptionManager.SolveHttp(ExceptionManager.GetExceptions("getMusicWithStream").LastOrDefault());
                            new AlertBox("Hata", msg).ShowDialog();
                        });
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
            window.sldPosition.Value = 0;
            window.lblState.Content = "Boş";
            window.lblStateContext.Content = "Boş";
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
            }
            else
            {
                window.btnPlay.FontSize = 40;
                window.btnPlay.Content = "∣∣";
                window.btnPlayContext.Content = "∣∣";
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
            await PlayMusic(base.Next(nIndex));
        }
        public async Task Previous()
        {
            if (!isLoaded)
                return;
            await PlayMusic(base.Previous());
        }
        #endregion

        #region Utils
        public async Task RefreshPlaying() => await lvPlaying.Dispatcher.InvokeAsync(() => lvPlaying.Items.Refresh());
        public void RemoveMusic(int index)
        {
            srcPlaying.DeleteItem(index);
            if (playingMusic.ID == index + 1)
                Play(srcPlaying[index].Item.Music);
        }
        public void SaveState()
        {
            if (isLoaded)
            {
                string listId = PlayingListID == -2 ? "---" + playingListYT : PlayingListID.ToString();
                string shuffle = isShuffled ? ShufflerToString() : null;
                int playing = playingMusic?.ID ?? 0;

                config.Set("LASTID", $"{listId}||{shuffle}||{playing}||{Position}");
            }
            else
                config.Set("LASTID", null);

            string x = ((int)window.volume) + "," + (int)window.sldVolume.Value;
            config.Set("VOL", x);
        }
        public async Task RestoreState() =>
            await Task.Run(async () =>
            {
                try
                {
                    string[] state = config.Get("LASTID").Split("||");
                    if (state.Length != 4)
                        return;

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
                        await PlayMusicList(list, false, state[1]));
                    int count = list.Musics.Count();
                    while (srcPlaying.Count != count) ;

                    if (!CheckShuffleCorrection(state[1]))
                    {
                        
                    }

                    try
                    {
                        int number = int.Parse(state[2]);
                        await PlayMusic(
                            srcPlaying[number <= 1 ? 0 : number], 
                            TimeSpan.ParseExact(state[3], @"hh\.mm\.ss", CultureInfo.CurrentCulture),
                            false);
                    }
                    catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidOperationException)
                    {

                    }
                    finally
                    {
                        while (!isLoaded) ;
                    }


                    App.Current.Dispatcher.Invoke(() =>
                    {
                        string[] vol = config.Get("VOL").Split(',');
                        window.SetVolume(int.Parse(vol[1]), int.Parse(vol[0]));

                        if (config.Get("PLINTRAY").ToLower() == "true")
                        {
                            Resume();
                            window.SwitchVisibility();
                        }
                        else
                            Pause();
                    });
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.StackTrace + "\n\n" + e.Message);
                }
            });
        private bool CheckShuffleCorrection(string q)
        {
            string[] splt = q.Split(',');
            return srcPlaying.Count != splt.Length || srcPlaying.All(x => splt.Any(y => y == (x.Index - 1).ToString()));
        }
        private void FixShuffle(string q)
        {
            string[] splt = q.Split(',');
            foreach (var x in srcPlaying)
                if (!splt.Contains((x.Index - 1).ToString()))
                    shuffler.Add(x);
            for (int i = 0; i < splt.Length; i++)
                if (!srcPlaying.Any(y => (y.Index - 1).ToString() == splt[i]))
                    shuffler.Remove(i);
        }
        public new void Shuffle(bool reverse) =>
            base.Shuffle(reverse);
        public IEnumerable<Orderable<ColorableMusic>> GetShuffledList() =>
            shuffler;
        #endregion
    }
}
