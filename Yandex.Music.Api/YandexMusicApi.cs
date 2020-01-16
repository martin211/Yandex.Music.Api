﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Yandex.Music.Api.Common;
using Yandex.Music.Api.Models;
using Yandex.Music.Api.Requests;
using Yandex.Music.Api.Requests.Account;
using Yandex.Music.Api.Requests.Album;
using Yandex.Music.Api.Requests.Auth;
using Yandex.Music.Api.Requests.Library;
using Yandex.Music.Api.Requests.Playlist;
using Yandex.Music.Api.Requests.Search;
using Yandex.Music.Api.Requests.Track;
using Yandex.Music.Api.Requests.Yandex;
using Yandex.Music.Api.Responses;

namespace Yandex.Music.Api
{
  public class YandexMusicApi : IYandexMusicApi
  {
    private YandexMusicSettings _settings { get; set; }
    public YUser User { get; set; }
    private HttpContext _httpContext;

    public YandexMusicApi()
    {
      _settings = new YandexMusicSettings();
      _httpContext = new HttpContext();
    }

    public IYandexMusicApi UseWebProxy(IWebProxy proxy)
    {
      _httpContext.WebProxy = proxy;

      return this;
    }

    public async Task<YAuthorizeResponse> AuthorizeAsync(string login, string password)
    {
      var request = new YAuthorizeRequest(_httpContext).Create(login, password);

      try
      {
        using (var response = (HttpWebResponse) await request.GetResponseAsync())
        {
          _httpContext.Cookies.Add(response.Cookies);

          if (response.ResponseUri == new Uri("https://pda-passport.yandex.ru/passport?mode=auth"))
          {
            return new YAuthorizeResponse
            {
              IsAuthorized = false,
              User = null
            };
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex);
        
        return new YAuthorizeResponse
        {
          IsAuthorized = false,
          User = null
        };
      }

      User = new YUser
      {
        Login = login,
        Password = password
      };

      var authInfo = await GetUserAuthAsync();
      var authUserDetails = await GetUserAuthDetailsAsync();
      var authUser = authUserDetails.User;

      User = new YUser
      {
        Uid = authUser.Uid,
        Login = authUser.Login,
        Password = password,
        Name = authUser.Name,
        Sign = authUser.Sign,
        DeviceId = authUser.DeviceId,
        FirstName = authUser.FirstName,
        LastName = authUser.LastName,
        Experiments = authUserDetails.Experiments,
        Lang = authInfo.Lang,
        Timestamp = authInfo.Timestamp,
        YandexId = authInfo.YandexuId
      };

      return new YAuthorizeResponse
      {
        IsAuthorized = true,
        User = User
      };
    }

    public YAuthorizeResponse Authorize(string login, string password)
    {
      return AuthorizeAsync(login, password).GetAwaiter().GetResult();
    }

    public YAlbumResponse GetAlbum(string albumId)
    {
      var request = new YGetAlbumRequest(_httpContext).Create(albumId, User.Lang);
      var album = default(YAlbumResponse);
      
      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var data = GetDataFromResponse(response);
        album = YAlbumResponse.FromJson(data);

        _httpContext.Cookies.Add(response.Cookies);
      }

      return album;
    }
    
    public YTrackResponse GetTrack(string trackId)
    {
      var request = new YGetTrackResponse(_httpContext).Create(trackId, User.Lang);
      var track = default(YTrackResponse);
      
      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var data = GetDataFromResponse(response)["track"];
        track = YTrackResponse.FromJson(data);

        _httpContext.Cookies.Add(response.Cookies);
      }

      return track;
    }
    
    public List<YTrackResponse> GetListFavorites(string login = null)
    {
      if (login == null)
        login = User.Login;

      var request = new YGetPlaylistFavoritesRequest(_httpContext).Create(login, User.Lang);
      var tracks = new List<YTrackResponse>();
      
      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var data = GetDataFromResponse(response);
        var jTracks = (JArray) data["tracks"];

        tracks = YTrackResponse.FromJsonArray(jTracks);

        _httpContext.Cookies.Add(response.Cookies);
      }

      return tracks;
    }

    public YPlaylistResponse GetPlaylistDejaVu()
    {
      var request = new YGetPlaylistDejaVuRequest(_httpContext).Create(User.Lang);
      var playlist = default(YPlaylistResponse);
      
      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var data = GetDataFromResponse(response);
        var jPlaylist = data["playlist"];
        playlist = YPlaylistResponse.FromJson(jPlaylist);

        _httpContext.Cookies.Add(response.Cookies);
      }

      return playlist;
    }
    
    public YPlaylistResponse GetPlaylistOfDay()
    {
      var request = new YGetPlaylistOfDayRequest(_httpContext).Create(User.Lang);
      var playlist = default(YPlaylistResponse);
      
      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var data = GetDataFromResponse(response);
        var jPlaylist = data["playlist"];
        playlist = YPlaylistResponse.FromJson(jPlaylist);

        _httpContext.Cookies.Add(response.Cookies);
      }

      return playlist;
    }

    public YTrackDownloadInfoResponse GetMetadataTrackForDownload(string trackKey, Int64 time)
    {
      var request = new YTrackDownloadInfoRequest(_httpContext).Create(trackKey, time, User.Uid, User.Login);
      var data = default(JToken);
      
      using (var response = (HttpWebResponse) request.GetResponse())
      {
        data = GetDataFromResponse(response);
        
        _httpContext.Cookies.Add(response.Cookies);
      }

      var trackInfo = YTrackDownloadInfoResponse.FromJson(data);

      return trackInfo;
    }

    public string BuildLinkForDownloadTrack(YTrackDownloadInfoResponse mainDownloadResponse, YStorageDownloadFileResponse storageDownloadResponse)
    {
      var path = storageDownloadResponse.Path;
      var host = storageDownloadResponse.Host;
      var ts = storageDownloadResponse.Ts;
      var s = storageDownloadResponse.S;
      var codec = mainDownloadResponse.Codec;
      
      var secret = $"XGRlBW9FXlekgbPrRHuSiA{path.Substring(1, path.Length-1)}{s}";
      var md5 = MD5.Create();
      var md5Hash = md5.ComputeHash(Encoding.UTF8.GetBytes(secret));
      var hmacsha1 = new HMACSHA1();
      var hmasha1Hash = hmacsha1.ComputeHash(md5Hash);
      var sign = BitConverter.ToString(hmasha1Hash).Replace("-", "").ToLower();
      
      var link = $"https://{host}/get-{codec}/{sign}/{ts}/{path}";

      return link;
    }

    public YStorageDownloadFileResponse GetDownloadFilInfo(YTrackDownloadInfoResponse metadataInfo, Int64 time)
    {
      var request = new YStorageDownloadFileRequest(_httpContext).Create(metadataInfo.Src, time, User.Login);
      var data = default(JToken);

      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var result = "";

        using (var stream = response.GetResponseStream())
        {
          var reader = new StreamReader(stream);

          result = reader.ReadToEnd();
        }

        data = JToken.Parse(result);

        _httpContext.Cookies.Add(response.Cookies);
      }

      return YStorageDownloadFileResponse.FromJson(data);
    }
    
    public void ExtractTrackToFile(string trackKey, string filePath)
    {
      var time = GetTInterval();
      var mainDownloadResponse = GetMetadataTrackForDownload(trackKey, time);
      var storageDownloadResponse = GetDownloadFilInfo(mainDownloadResponse, time);
      
      var fileLink = BuildLinkForDownloadTrack(mainDownloadResponse, storageDownloadResponse);
      
      try
      {
        using (var client = new WebClient())
        {
          client.DownloadFile(fileLink, filePath);
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.ToString());
      }
    }
    
//    public void ExtractStreamTrack(string trackKey)
//    {
//      var time = GetTInterval();
//      var mainDownloadResponse = GetMetadataTrackForDownload(trackKey, time);
//      var storageDownloadResponse = GetDownloadFilInfo(mainDownloadResponse, time);
      
//      var fileLink = BuildLinkForDownloadTrack(mainDownloadResponse, storageDownloadResponse);
      
//      try
//      {
//        using (var client = new WebClient())
//        {
//          client.DownloadFile(fileLink, filePath);
//        }
//      }
//      catch (Exception ex)
//      {
//        Console.WriteLine(ex.ToString());
//      }
//    }

    public byte[] ExtractDataTrack(string trackKey)
    {
      var time = GetTInterval();
      var mainDownloadResponse = GetMetadataTrackForDownload(trackKey, time);
      var storageDownloadResponse = GetDownloadFilInfo(mainDownloadResponse, time);
      
      var fileLink = BuildLinkForDownloadTrack(mainDownloadResponse, storageDownloadResponse);

      byte[] bytes = default(byte[]);
      
      try
      {
        using (var client = new WebClient())
        {
          bytes = client.DownloadData(fileLink);
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.ToString());
      }

      return bytes;
    }

    public YandexStreamTrack ExtractStreamTrack(string trackKey, int fileSize)
    {
      var time = GetTInterval();
      var mainDownloadResponse = GetMetadataTrackForDownload(trackKey, time);
      var storageDownloadResponse = GetDownloadFilInfo(mainDownloadResponse, time);
      
      var fileLink = BuildLinkForDownloadTrack(mainDownloadResponse, storageDownloadResponse);
      return YandexStreamTrack.Open(new Uri(fileLink), fileSize);
    }
    
//    public bool ExtractTrackToFile(YandexTrack track, string folder)
//    {
//      try
//      {
//        var trackDonloadInfo = GetDownloadTrackInfo(track.StorageDir);
//        var trackDownloadUrl = _settings.GetURLDownloadTrack(track, trackDonloadInfo);
//        var isNetworing = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        
//        using (var client = new WebClient())
//        {
//          client.DownloadFile(trackDownloadUrl, $"{folder}/{track.Title}.mp3");
//        }

//        return true;
//      }
//      catch (Exception ex)
//      {
//        Console.WriteLine(ex.ToString());
//      }

//      return false;
//    }

//    public YandexStreamTrack ExtractStreamTrack(YandexTrack track)
//    {
//      var trackDonloadInfo = GetDownloadTrackInfo(track.StorageDir);
//      var trackDownloadUrl = _settings.GetURLDownloadTrack(track, trackDonloadInfo);

//      var isNetworing = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

//      return YandexStreamTrack.Open(trackDownloadUrl, track.FileSize);
//    }

//    public byte[] ExtractDataTrack(YandexTrack track)
//    {
//      var trackDonloadInfo = GetDownloadTrackInfo(track.StorageDir);
//      var trackDownloadUrl = _settings.GetURLDownloadTrack(track, trackDonloadInfo);
//      byte[] bytes;
      
//      using (var client = new WebClient())
//      {
//        bytes = client.DownloadData(trackDownloadUrl);
//      }

//      return bytes;
//    }

    public List<YTrackResponse> SearchTrack(string trackName, int pageNumber = 0)
    {
      var tracks = Search(trackName, YandexSearchType.Tracks, pageNumber).Select(x => (YTrackResponse)x).ToList();

      return tracks;
    }

    public List<YArtistResponse> SearchArtist(string artistName, int pageNumber = 0)
    {
      var artists = Search(artistName, YandexSearchType.Artists, pageNumber).Select(x => (YArtistResponse)x).ToList();

      return artists;
    }

    public List<YPlaylistResponse> SearchPlaylist(string playlistName, int pageNumber = 0)
    {
      var playlists = Search(playlistName, YandexSearchType.Playlists, pageNumber).Select(x => (YPlaylistResponse)x).ToList();

      return playlists;
    }

    public List<YAlbumResponse> SearchAlbums(string albumName, int pageNumber = 0)
    {
      var albums = Search(albumName, YandexSearchType.Albums, pageNumber).Select(x => (YAlbumResponse)x).ToList();

      return albums;
    }
    
    public List<YUserResponse> SearchUsers(string userName, int pageNumber = 0)
    {
      var users = Search(userName, YandexSearchType.Users, pageNumber).Select(x => (YUserResponse)x).ToList();

      return users;
    }

    public List<IYandexSearchable> Search(string searchText, YandexSearchType searchType, int page = 0)
    {
      var listResult = new List<IYandexSearchable>();

      var request = new YSearchRequest(_httpContext).Create(searchText, searchType, page);

      using (var response = (HttpWebResponse) request.GetResponse())
      {
        var json = GetDataFromResponse(response);
        var fieldName = searchType.ToString().ToLower();
        var jArray = (JArray) json[fieldName]["items"];
        
        if (searchType == YandexSearchType.Tracks)
        {
          listResult = YTrackResponse.FromJsonArray(jArray).Select(x => (IYandexSearchable) x).ToList();
        } 
        else if (searchType == YandexSearchType.Artists)
        {
          listResult = YArtistResponse.FromJsonArray(jArray).Select(x => (IYandexSearchable) x).ToList();
        }
        else if (searchType == YandexSearchType.Albums)
        {
          listResult = YAlbumResponse.FromJsonArray(jArray).Select(x => (IYandexSearchable) x).ToList();
        }
        else if (searchType == YandexSearchType.Playlists)
        {
          listResult = YPlaylistResponse.FromJsonArray(jArray).Select(x => (IYandexSearchable) x).ToList();
        }
        else if (searchType == YandexSearchType.Users)
        {
          listResult = YUserResponse.FromJsonArray(jArray).Select(x => (IYandexSearchable) x).ToList();
        }
      }

      return listResult;
    }

//    protected YandexTrackDownloadInfo GetDownloadTrackInfo(string storageDir)
//    {
//      var fileName = GetDownloadTrackInfoFileName(storageDir);
//      var request = GetRequest(_settings.GetDownloadTrackInfoURL(storageDir, fileName));
//      var trackDownloadInfo = new YandexTrackDownloadInfo();

//      using (var response = (HttpWebResponse) request.GetResponse())
//      {
//        using (var stream = response.GetResponseStream())
//        {
//          var reader = new StreamReader(stream);
//          var sourceText = reader.ReadToEnd();
          
//          var xElem = XDocument.Parse(sourceText).Root;
//          var elements = new Dictionary<string, string>();
//          for (var x = (XElement) xElem.FirstNode; x != null; x = (XElement) x.NextNode)
//          {
//            elements.Add(x.Name.LocalName, x.Value);
//          }
//          _cookies.Add(response.Cookies);

//          trackDownloadInfo.Host = elements["host"];
//          trackDownloadInfo.Path = elements["path"];
//          trackDownloadInfo.Ts = elements["ts"];
//          trackDownloadInfo.Region = elements["region"];
//          trackDownloadInfo.S = elements["s"];
//        }
//      }

//      return trackDownloadInfo;
//    }

//    protected string GetDownloadTrackInfoFileName(string storageDir)
//    {
//      var url = _settings.GetURLDownloadFile(storageDir);
//      var request = GetRequest(url, WebRequestMethods.Http.Get);
//      var fileName = "";
//      var trackLength = 0;
      
//      using (var response = (HttpWebResponse) request.GetResponse())
//      {
//        using (var stream = response.GetResponseStream())
//        {
//          var reader = new StreamReader(stream);
//          var sourceText = reader.ReadLine();
//          sourceText = reader.ReadLine();

//          var xElem = XDocument.Parse(sourceText).Root;
//          var attrs = xElem.Attributes()
//            .Where(a => !a.IsNamespaceDeclaration)
//            .Select(a => new XAttribute(a.Name.LocalName, a.Value))
//            .ToList();

//          _cookies.Add(response.Cookies);
//          fileName = attrs.First().Value;
//          trackLength = int.Parse(attrs.Last().Value.ToString());
//        }
//      }

//      return fileName;
//    }

    protected JToken GetDataFromResponse(HttpWebResponse response)
    {
      var result = "";

      using (var stream = response.GetResponseStream())
      {
        var reader = new StreamReader(stream);

        result = reader.ReadToEnd();
      }
      
      return JToken.Parse(result);
    }

    public YAccountResponse GetAccounts()
    {
      try
      {
        var result = "";
        var request = new YGetAccountRequest(_httpContext).Create(User.Lang);

        using (var response = (HttpWebResponse) request.GetResponse())
        {
          using (var stream = response.GetResponseStream())
          {
            var reader = new StreamReader(stream);

            result = reader.ReadToEnd();
          }

          _httpContext.Cookies.Add(response.Cookies);
        }

        var json = JToken.Parse(result);
        var account = YAccountResponse.FromJson(json);

        return account;
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex);
      }

      return null;
    }

    public Int64 GetTInterval()
    {
      DateTime dt = TimeZoneInfo.ConvertTimeToUtc(DateTime.Now);
      DateTime dt1970 = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
      TimeSpan tsInterval = dt.Subtract(dt1970);
      Int64 iMilliseconds = Convert.ToInt64(tsInterval.TotalMilliseconds);

      return iMilliseconds;
    }

    public YLibraryResponse GetLibrary(string ownerUid)
    {
      var request = new YGetLibraryRequest(_httpContext).Create(User.Login, User.Lang, User.Uid);

      var result = string.Empty;

      using (var response = (HttpWebResponse) request.GetResponse())
      {
        using (var stream = response.GetResponseStream())
        {
          var reader = new StreamReader(stream);

          result = reader.ReadToEnd();
        }

        _httpContext.Cookies.Add(response.Cookies);
      }

      var json = JToken.Parse(result);
      var library = YLibraryResponse.FromJson(json);

      return library;
    }

    public async Task<YAuthInfoResponse> GetUserAuthAsync()
    {
      var request = new YGetAuthInfoRequest(_httpContext).Create(User.Login, GetTInterval());

      var result = string.Empty;

      using (var response = (HttpWebResponse) await request.GetResponseAsync())
      {
        using (var stream = response.GetResponseStream())
        {
          var reader = new StreamReader(stream);

          result = await reader.ReadToEndAsync();
        }

        _httpContext.Cookies.Add(response.Cookies);
      }

      var json = JToken.Parse(result);
      var authInfoResponse = YAuthInfoResponse.FromJson(json);

      return authInfoResponse;
    }
    
    public YAuthInfoResponse GetUserAuth()
    {
      return GetUserAuthAsync().GetAwaiter().GetResult();
    }

    public async Task<YAuthInfoUserResponse> GetUserAuthDetailsAsync()
    {
      var request = new YGetAuthInfoUserRequest(_httpContext).Create(User.Uid, User.Login, User.Lang);
      var result = string.Empty;

      using (var response = (HttpWebResponse) await request.GetResponseAsync())
      {
        using (var stream = response.GetResponseStream())
        {
          var reader = new StreamReader(stream);

          result = await reader.ReadToEndAsync();
        }

        _httpContext.Cookies.Add(response.Cookies);
      }

      var json = JToken.Parse(result);
      var authInfoUserResponse = YAuthInfoUserResponse.FromJson(json);

      return authInfoUserResponse;
    }
    
    public YAuthInfoUserResponse GetUserAuthDetails()
    {
      return GetUserAuthDetailsAsync().GetAwaiter().GetResult();
    }

    public YPlaylistChangeResponse CreatePlaylist(string name)
    {
//      var getCookiet = GetYandexCookie();
//      var auth2 = GetAuthV2();
//      var accounts = GetAccounts();
//      var library = GetLibrary(accounts.DefaultUID);
//      var auth = GetUserAuthDetails(accounts.DefaultUID);

      try
      {
        var request = new YPlaylistChangeRequest(_httpContext).Create(name, User.Sign, User.Experiments, User.Uid, User.Login);
        var result = string.Empty;

        using (var response = (HttpWebResponse) request.GetResponse())
        {
          using (var stream = response.GetResponseStream())
          {
            var reader = new StreamReader(stream);

            result = reader.ReadToEnd();
          }

          _httpContext.Cookies.Add(response.Cookies);
        }

        var json = JToken.Parse(result);
        return YPlaylistChangeResponse.FromJson(json);
      }
      catch (WebException ex)
      {
        Console.WriteLine(ex);
      }

      return new YPlaylistChangeResponse
      {
        Success = false,
        Playlist = null
      };
    }

    public bool RemovePlaylist(long kind)
    {
      try
      {
//        var accounts = GetAccounts();
//        var auth = GetUserAuthDetails(accounts.DefaultUID);

        var request = new YPlaylistRemoveRequest(_httpContext).Create(kind, User.Sign, User.Experiments, User.Uid, User.Login);
        request.GetResponse();
        
        return true;
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex);
      }

      return false;
    }

    public YGetCookieResponse GetYandexCookie()
    {
      try
      {
        var request = new YGetCookieRequest(_httpContext).Create(User.Login);
//        request.ProtocolVersion = new Version(2, 0);
//        request.Headers.Add(":method", "GET");
//        request.Headers.Add(":authority", "matchid.adfox.yandex.ru");
//        request.Headers.Add(":path", "/getcookie");
//        request.Headers.Add(":scheme", "https");
        

        var result = "";
        
        using (var response = (HttpWebResponse) request.GetResponse())
        {
          using (var stream = response.GetResponseStream())
          {
            var reader = new StreamReader(stream);

            result = reader.ReadToEnd();
          }

          _httpContext.Cookies.Add(response.Cookies);
        }

        var json = JToken.Parse(result);
        return YGetCookieResponse.FromJson(json);
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex);
      }

      return null;
    }

    #region AddLike
    public YSetLikedTrackResponse SetLikedTrack(string trackKey, bool value)
    {
      var request = new YSetLikedTrackRequest(_httpContext).Create(value, trackKey, GetTInterval(), User.Sign, User.Uid, User.Login);
      var setLikedResponse = default(YSetLikedTrackResponse);
      
      try
      {
        var result = string.Empty;

        using (var response = (HttpWebResponse) request.GetResponse())
        {
          using (var stream = response.GetResponseStream())
          {
            var reader = new StreamReader(stream);

            result = reader.ReadToEnd();
          }

          _httpContext.Cookies.Add(response.Cookies);
        }

        var json = JToken.Parse(result);
        setLikedResponse = YSetLikedTrackResponse.FromJson(json);
//        return YPlaylistChangeResponse.FromJson(json);
        Console.WriteLine("123");
      }
      catch (WebException ex)
      {
        Console.WriteLine(ex);
      }

//      var changeLikeResponse = ChangeLikedTrack(trackKey, value);

      return setLikedResponse;
    }

    public YAddLikedTrackResponse ChangeLikedTrack(string trackKey, bool value)
    {
      var request = new YAddLikedTrackRequest(_httpContext).Create(value, trackKey, GetTInterval(), User.Sign, User.Uid, User.Login);
      
      try
      {
        var result = string.Empty;

        using (var response = (HttpWebResponse) request.GetResponse())
        {
          using (var stream = response.GetResponseStream())
          {
            var reader = new StreamReader(stream);

            result = reader.ReadToEnd();
          }

          _httpContext.Cookies.Add(response.Cookies);
        }

        var json = JToken.Parse(result);
        return YAddLikedTrackResponse.FromJson(json);
      }
      catch (WebException ex)
      {
        Console.WriteLine(ex);
      }

      return new YAddLikedTrackResponse
      {
        Success = false,
        Act = null
      };
    }
    #endregion
  }
}