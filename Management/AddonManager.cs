/*
 Corelium Development - Tüm Hakları Saklıdır @ 2021
 Furkan M. Yılmaz
*/

using CynthCore;
using CynthCore.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace CynthMusic.Management
{
    public class AddonManager
    {
        private PlayerService playerService => MainWindow.playerService;
        private MusicService musicService;
        private PlaylistManager playlistManager => MainWindow.playlistManager;

        private OrderableCollection<Location> srcLocations;
        private OrderableCollection<IMusic> srcFavourites;

        public AddonManager(ref ListView lvLocations, ref ListView lvFavourites, MusicService musicService)
        {
            this.musicService = musicService;
            srcLocations = new OrderableCollection<Location>();
            srcFavourites = new OrderableCollection<IMusic>();
            lvLocations.ItemsSource = srcLocations;
            lvFavourites.ItemsSource = srcFavourites;
        }

        #region Locations
        public async Task LoadLocations()
        {
            srcLocations.Clear();
            await foreach (var x in playlistManager.GetLocationsAsync())
                srcLocations.Add(x);
        }
        public async Task<bool> AddLocation(string location)
        {
            if (!System.IO.Directory.Exists(location) || await playlistManager.ContainsLocation(location))
                return false;
            srcLocations.Add(new Location { ID = await playlistManager.AddLocation(location), Path = location });
            return true;
        }
        public async Task<bool> AddPlaylistFromLocation(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || await playlistManager.Contains(name))
                return false;
            var list = playlistManager.GetFromPath(path, false, name).ElementAt(0);
            if (list.Musics.Count() == 0)
                return false;
            list.Author = Environment.UserName;
            await playlistManager.AddPlaylistAsync(list);
            return true;
        }
        public async Task DeleteLocation(Button btn)
        {
            var item = srcLocations.ObjectConvert(btn.DataContext);
            await playlistManager.DeleteLocation(item.Value.Item.ID);
            srcLocations.DeleteItem(item.Value);
        }
        public async Task<IMusic> GetMusicAsync(string location, bool isYouTube) =>
            isYouTube ? await musicService.GetConvertedYouTubeMusicAsync(location) : musicService.Get(location);
        #endregion

        #region Favourites
        public async Task LoadFavourites()
        {
            srcFavourites.Clear();
            await foreach (var x in musicService.GetFavourites())
                srcFavourites.Add(x);
        }
        public async Task AddFavourite(Button btn)
        {
            var item = (Orderable<ColorableMusic>)btn.DataContext;

            var state = await musicService.AddFavouriteAsync(item.Item.Music.Name, item.Item.Music.Author, item.Item.Music.SaveIdentity);
            btn.Tag = state.Item2;
            if (!state.Item2)
                await DelFav(item.Item.Music);
            else
            {
                var f = item.Item.Music;
                f.ID = state.Item1;
                srcFavourites.Add(f);
            }
        }
        public async Task DeleteFavourite(Button btn)
        {
            var item = (Orderable<IMusic>)btn.DataContext;
            await DelFav(item.Item);
        }
        private async Task DelFav(IMusic item)
        {
            var find = srcFavourites.FirstOrDefault(x => x.Item.Equals(item));
            await musicService.RemoveFavouriteAsync(find.Item.ID);
            srcFavourites.DeleteItem(find);

            Orderable<ColorableMusic>? play = playerService.srcPlaying.FirstOrDefault(x => x.Item.Music.Equals(item));
            if (!play.HasValue || play.Value.Item.Music == null)
                return;
            play.Value.Item.Music.IsFavourite = false;
            playerService.srcPlaying[play.Value.Index - 1] = play.Value;
            await playerService.RefreshPlaying();
        }
        public IEnumerable<IMusic> GetAllFavourites() =>
            srcFavourites.Select(x => x.Item);
        #endregion

    }
}
