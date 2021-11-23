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

        public OrderableCollection<ColorableMusic> srcPlaying;
        public static int PlayingListID = -1;

        private List<int> toDeleteList;
        private string playingListYT;

        public PlayerService(ref ListView lvPlaying, ref MediaElement player, MusicService musicService, Action<string> setIconTip) :
            base(ref player, setIconTip)
        {
            this.musicService = musicService;
            this.lvPlaying = lvPlaying;

            cancellation = new CancellationTokenSource();
            srcPlaying = new OrderableCollection<ColorableMusic>();
            toDeleteList = new List<int>();
            lvPlaying.ItemsSource = srcPlaying;
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

        #region Play
        private async Task PlayNormalMusicList(MusicList list, bool play)
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
            if (srcPlaying.Count != 0 && play)
                await PlayMusicWithLoad(srcPlaying.FirstOrDefault());
        }
        private async Task PlayYouTubeMusicList(YouTubeMusicList list, CancellationToken token, bool play) =>
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
                        await PlayMusicWithLoad(srcPlaying.FirstOrDefault());
                }
            });
        public async Task PlayMusicList(IMusicList list, bool play = true, IEnumerable<int> shuffle = null)
        {
            srcPlaying.Clear();
            Stop();
            if (shuffle != null)
            {
                Shuffle(true, shuffle);
                window.btnShuffle.Foreground = new LinearGradientBrush(Colors.Red, Colors.Aqua, 45);
            }

            PlayingListID = list.ID == -1 ? -2 : list.ID;
            SaveState(false);
            if (list is YouTubeMusicList l)
                await PlayYouTubeMusicList(l, GetToken(), play);
            else
                await PlayNormalMusicList((MusicList)list, play);

        }
        public async Task PlayMusicWithLoad(Orderable<ColorableMusic> music, TimeSpan? position = null, bool start = true) =>
            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (music.Item.Music is not YouTubeMusic)
                {
                    Play(music.Item.Music.SaveIdentity, position);
                    return;
                }
                var m = await musicService.GetConvertedYouTubeMusicAsync(((YouTubeMusic)music.Item.Music).YouTubeUri);
                if (m == null)
                {
                    string msg = ExceptionManager.SolveHttp(ExceptionManager.GetExceptions("getMusicWithStream").LastOrDefault());
                    new AlertBox("Hata", msg).ShowDialog();
                    return;
                }
                var item = srcPlaying[music.Index - 1];
                item.Item.Music.PlayURL = m.Value.PlayURL;
                item.Item.Music.Length = m.Value.Length;
                srcPlaying.SetItem(music.Index - 1, item);
                lvPlaying.Items.Refresh();
                Play(music.Item.Music.SaveIdentity, position);
            });
        public async Task PlayLocation(Button btn)
        {
            var item = (Orderable<Location>)btn.DataContext;
            var list = playlistManager.GetFromPath(item.Item.Path, false).ElementAt(0);
            if (list.Musics.Count() == 0)
                return;
            await PlayMusicList(list);
        }
        public async Task PlayLoadedList() =>
            await PlayMusicList(await GetLoadedList());
        public async Task PlayFavourites()
        {
            Stop();
            srcPlaying.Clear();
            foreach (var x in addon.GetAllFavourites())
                srcPlaying.Add(new ColorableMusic(x));
            await PlayMusicWithLoad(srcPlaying.FirstOrDefault());
        }
        #endregion

        #region Utils
        public async Task RefreshPlaying() => await lvPlaying.Dispatcher.InvokeAsync(() => lvPlaying.Items.Refresh());
        public void RemoveMusic(int index)
        {
            srcPlaying.DeleteItem(index);
            if (GetPlayingID() == index + 1)
                ConvertPlay(srcPlaying[index]);
        }
        public void SaveState(bool first = true)
        {
            if (!IsPlayingList)
            {
                config.Set("LASTID", null);
                return;
            }
            string listId = PlayingListID == -2 ? "---" + playingListYT : PlayingListID.ToString();
            string shuffle = isShuffled ? string.Join(',', playingListOrders) : null;
            int playing = GetPlayingID() ?? 0;
            string pos = GetPosition();

            config.Set("LASTID", $"{listId}||{shuffle}||{playing}||{pos}");
            if (!first)
                return;
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
                        await PlayMusicList(list, false, !string.IsNullOrWhiteSpace(state[1]) ? state[1].Split(',').Select(x => int.Parse(x)) : null));
                    int count = list.Musics.Count();
                    while (srcPlaying.Count != count) ;

                    try
                    {
                        int number = int.Parse(state[2]);
                        await PlayMusicWithLoad(srcPlaying[number <= 1 ? 0 : number - 1], TimeSpan.ParseExact(state[3], @"hh\.mm\.ss", CultureInfo.CurrentCulture), false);
                    }
                    catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidOperationException)
                    {

                    }
                    finally
                    {
                        while (!isPlaying) ;
                    }

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        string[] vol = config.Get("VOL").Split(',');
                        window.SetVolume(int.Parse(vol[1]), int.Parse(vol[0]));

                        if (config.Get("PLINTRAY").ToLower() == "true")
                            window.SwitchVisibility();
                        else
                        {
                            window.SetPlayState(false);
                            TogglePlay(false);
                        }
                    });
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.StackTrace + "\n\n" + e.Message);
                }
            });
        #endregion
    }
}
