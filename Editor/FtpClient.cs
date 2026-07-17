using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace DivineDragon
{
    // Cached FTP reachability for the active host, so the Build menu validator can grey out
    // without doing a live connection (validators fire far too often for that).
    public static class FtpStatus
    {
        private static string _key;
        private static bool _reachable;

        private static string Key(string host, int port)
        {
            return (host ?? "") + ":" + port;
        }

        public static void Set(string host, int port, bool reachable)
        {
            _key = Key(host, port);
            _reachable = reachable;
        }

        // Only trust the cached flag if it was recorded for this exact host and port.
        public static bool IsReachable(string host, int port)
        {
            return _reachable && _key == Key(host, port);
        }
    }

    // Small FTP helper built on the runtime's FtpWebRequest (works fine on Unity's Mono).
    // Enough to list, create, upload and delete under the console's engage/mods folder.
    // Paths passed in are relative to the FTP root and use forward slashes.
    public class FtpClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _user;
        private readonly string _pass;
        private readonly int _timeoutMs;

        public FtpClient(string host, int port, bool anonymous, string user, string pass, int timeoutMs = 8000)
        {
            _host = host;
            _port = port <= 0 ? 5000 : port;
            if (anonymous)
            {
                // Most servers accept anything for an anonymous login, this is the usual pair.
                _user = "anonymous";
                _pass = "anonymous@";
            }
            else
            {
                _user = user ?? "";
                _pass = pass ?? "";
            }
            _timeoutMs = timeoutMs;
        }

        private Uri MakeUri(string remotePath)
        {
            string clean = (remotePath ?? "").Replace("\\", "/").TrimStart('/');
            // UriBuilder handles escaping spaces and other characters in the path for us.
            return new UriBuilder("ftp", _host, _port, clean).Uri;
        }

        // FtpWebRequest lists the parent directory unless the path ends with a slash, so add one.
        private static string AsDir(string remoteDir)
        {
            string clean = (remoteDir ?? "").Replace("\\", "/").TrimEnd('/');
            return clean.Length == 0 ? "/" : clean + "/";
        }

        private FtpWebRequest Make(string remotePath, string method)
        {
            var req = (FtpWebRequest)WebRequest.Create(MakeUri(remotePath));
            req.Method = method;
            req.Credentials = new NetworkCredential(_user, _pass);
            req.UseBinary = true;
            req.UsePassive = true;
            req.KeepAlive = true;
            req.Timeout = _timeoutMs;
            req.ReadWriteTimeout = _timeoutMs;
            return req;
        }

        // Bare names of everything in a directory. Good enough for engage/mods, which only holds
        // mod folders. Servers sometimes echo full paths, so we keep just the last segment.
        public List<string> ListNames(string remoteDir)
        {
            var names = new List<string>();
            var req = Make(AsDir(remoteDir), WebRequestMethods.Ftp.ListDirectory);
            using (var resp = (FtpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string name = line.Trim().Replace("\\", "/").TrimEnd('/');
                    if (name.Length == 0)
                        continue;
                    int slash = name.LastIndexOf('/');
                    if (slash >= 0)
                        name = name.Substring(slash + 1);
                    if (name == "." || name == "..")
                        continue;
                    names.Add(name);
                }
            }
            return names;
        }

        // Detailed listing so we can tell folders from files when deleting a mod recursively.
        // Assumes a unix-style listing (what sys-ftpd and most Switch FTP servers produce).
        public List<KeyValuePair<string, bool>> ListDetailed(string remoteDir)
        {
            var entries = new List<KeyValuePair<string, bool>>();
            var req = Make(AsDir(remoteDir), WebRequestMethods.Ftp.ListDirectoryDetails);
            using (var resp = (FtpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                        continue;
                    bool isDir = line[0] == 'd';
                    string name = ParseNameFromDetail(line);
                    if (string.IsNullOrEmpty(name) || name == "." || name == "..")
                        continue;
                    entries.Add(new KeyValuePair<string, bool>(name, isDir));
                }
            }
            return entries;
        }

        // In a unix listing the name is everything after the 8th whitespace field, so a name with
        // spaces survives intact.
        private static string ParseNameFromDetail(string line)
        {
            int i = 0;
            int len = line.Length;
            for (int field = 0; field < 8 && i < len; field++)
            {
                while (i < len && char.IsWhiteSpace(line[i])) i++;
                while (i < len && !char.IsWhiteSpace(line[i])) i++;
            }
            while (i < len && char.IsWhiteSpace(line[i])) i++;
            return i < len ? line.Substring(i) : "";
        }

        // Create every segment of a path in turn, ignoring "already exists" errors.
        public void EnsureDirectory(string remoteDir)
        {
            string clean = (remoteDir ?? "").Replace("\\", "/").Trim('/');
            if (clean.Length == 0)
                return;

            string path = "";
            foreach (var part in clean.Split('/'))
            {
                path = path.Length == 0 ? part : path + "/" + part;
                TryMakeDir(path);
            }
        }

        private void TryMakeDir(string remoteDir)
        {
            try
            {
                var req = Make(remoteDir, WebRequestMethods.Ftp.MakeDirectory);
                using ((FtpWebResponse)req.GetResponse()) { }
            }
            catch (WebException)
            {
                // Almost always means the folder is already there, which is fine.
            }
        }

        public void UploadFile(string localFile, string remoteFile)
        {
            var req = Make(remoteFile, WebRequestMethods.Ftp.UploadFile);
            using (var stream = req.GetRequestStream())
            using (var file = File.OpenRead(localFile))
            {
                file.CopyTo(stream);
            }
            using ((FtpWebResponse)req.GetResponse()) { }
        }

        public void DeleteFile(string remoteFile)
        {
            var req = Make(remoteFile, WebRequestMethods.Ftp.DeleteFile);
            using ((FtpWebResponse)req.GetResponse()) { }
        }

        public void RemoveDirectory(string remoteDir)
        {
            var req = Make(remoteDir, WebRequestMethods.Ftp.RemoveDirectory);
            using ((FtpWebResponse)req.GetResponse()) { }
        }

        // Rename a file or folder. newName is just the new leaf name, kept in the same parent
        // (FtpWebRequest changes to the item's directory before issuing the rename).
        public void Rename(string remotePath, string newName)
        {
            var req = Make(remotePath, WebRequestMethods.Ftp.Rename);
            req.RenameTo = newName;
            using ((FtpWebResponse)req.GetResponse()) { }
        }

        // FTP can't remove a non-empty folder, so clear it out depth-first first.
        public void DeleteDirectoryRecursive(string remoteDir)
        {
            string baseDir = remoteDir.Replace("\\", "/").TrimEnd('/');
            foreach (var entry in ListDetailed(baseDir))
            {
                string child = baseDir + "/" + entry.Key;
                if (entry.Value)
                    DeleteDirectoryRecursive(child);
                else
                    DeleteFile(child);
            }
            RemoveDirectory(baseDir);
        }

        // Cheap reachability check, throws if the server can't be reached or login fails.
        public void TestConnection()
        {
            var req = Make("", WebRequestMethods.Ftp.PrintWorkingDirectory);
            using ((FtpWebResponse)req.GetResponse()) { }
        }
    }
}
