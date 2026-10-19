using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BaiduMapsDesktop
{
    [DataContract]
    internal sealed class PlaceEntry
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Url { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public string SavedAt { get; set; }
        public string Subtitle { get { return Kind == "search" ? "重新搜索这个地点" : "回到收藏的地图位置"; } }
    }
    [DataContract]
    internal sealed class UserLibrary
    {
        [DataMember] public List<PlaceEntry> Recent = new List<PlaceEntry>();
        [DataMember] public List<PlaceEntry> Favorites = new List<PlaceEntry>();
    }
    internal sealed class LibraryStore
    {
        private readonly string path;
        internal UserLibrary Data { get; private set; }
        internal LibraryStore(string file = null)
        {
            path = file ?? Path.Combine(Bootstrap.DataRoot, "library.json");
            try { using (var stream = File.OpenRead(path)) Data = (UserLibrary)new DataContractJsonSerializer(typeof(UserLibrary)).ReadObject(stream); }
            catch { Data = new UserLibrary(); }
            if (Data == null) Data = new UserLibrary();
            if (Data.Recent == null) Data.Recent = new List<PlaceEntry>();
            if (Data.Favorites == null) Data.Favorites = new List<PlaceEntry>();
            Data.Recent = Data.Recent.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name)).Take(30).ToList();
            Data.Favorites = Data.Favorites.Where(e => e != null && NavigationPolicy.IsTrusted(e.Url)).ToList();
        }
        internal void AddSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            query = query.Trim();
            if (query.Length > 200) query = query.Substring(0, 200);
            Data.Recent.RemoveAll(e => string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase));
            Data.Recent.Insert(0, new PlaceEntry { Name = query, Kind = "search", SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
            if (Data.Recent.Count > 30) Data.Recent.RemoveRange(30, Data.Recent.Count - 30);
            Save();
        }
        internal void AddFavorite(string name, string address)
        {
            if (!NavigationPolicy.IsTrusted(address)) return;
            name = string.IsNullOrWhiteSpace(name) ? "当前地图" : name.Trim();
            if (name.Length > 100) name = name.Substring(0, 100);
            Data.Favorites.RemoveAll(e => string.Equals(e.Url, address, StringComparison.Ordinal));
            Data.Favorites.Insert(0, new PlaceEntry { Name = name, Url = address, Kind = "favorite", SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
            Save();
        }
        internal void Remove(PlaceEntry entry)
        {
            if (entry == null) return;
            Data.Recent.Remove(entry); Data.Favorites.Remove(entry); Save();
        }
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(UserLibrary)).WriteObject(stream, Data);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception ex) { Diagnostics.Write("library-save-failed", ex.GetType().Name); }
        }
    }
    [DataContract]
    internal sealed class PageMessage
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "query")] public string Query { get; set; }
    }
}
