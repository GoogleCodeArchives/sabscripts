﻿// /*
//  *   Sab SABSync: Automatic TV Sync for SAB http://sabscripts.googlecode.com
//  *
//  * 
//  *   This program is free software: you can redistribute it and/or modify
//  *   it under the terms of the GNU General Public License as published by
//  *   the Free Software Foundation, either version 3 of the License, or
//  *   (at your option) any later version.
//  *
//  *   This program is distributed in the hope that it will be useful,
//  *   but WITHOUT ANY WARRANTY; without even the implied warranty of
//  *   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  *   GNU General Public License for more details.
//  *
//  *   You should have received a copy of the GNU General Public License
//  *   along with this program.  If not, see <http://www.gnu.org/licenses/>.
//  * 
//  */



using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using Rss;

namespace SABSync
{
    internal class Program
    {
        private static DirectoryInfo _tvRoot;
        private static DirectoryInfo _nzbDir;
        private static string _ignoreSeasons;
        private static string _tvTemplate;
        private static string _tvDailyTemplate;
        private static string[] _videoExt;
        private static FileInfo _rss;
        private static List<DirectoryInfo> _wantedShowNames;
        private static bool _sabReplaceChars;
        private static string _sabRequest;
        private static readonly List<string> Queued = new List<string>();
        private static readonly List<string> Summary = new List<string>();
        private static readonly FileInfo LogFile = new FileInfo(new FileInfo(Process.GetCurrentProcess().MainModule.FileName).Directory.FullName + "\\log\\" + DateTime.Now.ToString("MM.dd-HH-mm") + ".txt");
        private static void Main()
        {
            Stopwatch sw = Stopwatch.StartNew();

            //Create log dir if it doesn't exist
            if (!LogFile.Directory.Exists)
            {
                LogFile.Directory.Create();
            }

            Log("=====================================================================");
            Log("Starting " + Assembly.GetExecutingAssembly().GetName().Name + ". v" + Assembly.GetExecutingAssembly().GetName().Version + " - Build Date: " + new FileInfo(Process.GetCurrentProcess().MainModule.FileName).LastWriteTime.ToLongDateString());
            Log("Current System Time: {0}", DateTime.Now);
            Log("=====================================================================");

            try
            {
                LoadConfig();

                var reports = GetReports();

                Log("Watching {0} shows", _wantedShowNames.Count);
                Log("_ignoreSeasons: {0}", _ignoreSeasons);

                foreach (var report in reports)
                {
                    if (IsEpisodeWanted(report.Value, report.Key))
                    {
                        string queueResponse = AddToQueue(report.Key);
                        Queued.Add(report.Value + ": " + queueResponse);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message, true);
                Log(ex.ToString(), true);
            }


            sw.Stop();
            Log("=====================================================================" + Environment.NewLine);

            foreach (var logItem in Summary)
            {
                Log(logItem);
            }

            foreach (var item in Queued)
            {
                Log("Queued for download: " + item);
            }
            Log("Number of reports added to the queue: " + Queued.Count);

            Log("Process successfully completed. Duration {0:##.#}s", sw.Elapsed.TotalSeconds);
            Log(DateTime.Now.ToString());
        }

        private static void LoadConfig()
        {
            Log("Loading configuration...");

            _tvRoot = new DirectoryInfo(ConfigurationManager.AppSettings["tvRoot"]); //Get _tvRoot from app.config
            if (!_tvRoot.Exists)
                throw new ApplicationException("Invalid TV Root folder. " + _tvRoot);


            _wantedShowNames =new List<DirectoryInfo> (_tvRoot.GetDirectories());
            _rss = new FileInfo(ConfigurationManager.AppSettings["rss"]); //Get rss config file from app.config
            if (!_rss.Exists)
                throw new ApplicationException("Invalid RSS file path. " + _rss);

            _ignoreSeasons = ConfigurationManager.AppSettings["ignoreSeasons"]; //Get _ignoreSeasons from app.config

            _videoExt = ConfigurationManager.AppSettings["videoExt"].Trim(';', ' ').Split(';'); //Get _videoExt from app.config

            _tvTemplate = ConfigurationManager.AppSettings["tvTemplate"]; //Get _tvTemplate from app.config
            if (String.IsNullOrEmpty(_tvTemplate))
                throw new ApplicationException("Undefined tvTemplate");


            _tvDailyTemplate = ConfigurationManager.AppSettings["tvDailyTemplate"];
            //Get _tvDailyTemplate from app.config
            if (String.IsNullOrEmpty(_tvTemplate))
                throw new ApplicationException("tvDailyTemplate");

            _sabReplaceChars = Convert.ToBoolean(ConfigurationManager.AppSettings["sabReplaceChars"]);


            //Generate template for a sab request.
            string sabnzbdInfo = ConfigurationManager.AppSettings["sabnzbdInfo"]; //Get sabnzbdInfo from app.config
            string priority = ConfigurationManager.AppSettings["priority"]; //Get priority from app.config
            string apiKey = ConfigurationManager.AppSettings["apiKey"];
            string username = ConfigurationManager.AppSettings["username"]; //Get username from app.config
            string password = ConfigurationManager.AppSettings["password"]; //Get password from app.config
            _sabRequest =
                string.Format("http://{0}/api?$Action&priority={1}&apikey={2}&ma_username={3}&ma_password={4}",
                              sabnzbdInfo, priority, apiKey, username, password).Replace("$Action", "{0}");
            //Create URL String

            _nzbDir = new DirectoryInfo(ConfigurationManager.AppSettings["nzbDir"]); //Get _nzbDir from app.config
        }

        private static Dictionary<Int64, string> GetReports()
        {
            Log("Loading RSS feed list from {0}", _rss.FullName);

            var feeds = File.ReadAllLines(_rss.FullName);

            Dictionary<Int64, string> reports = new Dictionary<Int64, string>();

            foreach (var s in feeds)
            {
                var feedParts = s.Split('|');
                string url = feedParts[0];
                string name = "UN-NAMED";

                if (feedParts.Length > 1)
                {
                    name = feedParts[0];
                    url = feedParts[1];
                }

                Log("Downloading feed {0} from {1}", name, url);

                RssFeed feed = RssFeed.Read(url);
                RssChannel channel = feed.Channels[0];


                foreach (RssItem item in channel.Items)
                {
                    if (!item.Title.EndsWith("(Passworded)", StringComparison.InvariantCultureIgnoreCase))
                    {
                        Int64 reportId = Convert.ToInt64(Regex.Match(item.Link.AbsolutePath, @"\d{7,10}").Value);

                        //Don't add duplicated items
                        if (!reports.ContainsKey(reportId) && !reports.ContainsValue(item.Title))
                        {
                            reports.Add(reportId, item.Title);
                            Log("{0}:{1}", reportId, item.Title);
                        }
                    }
                    else
                    {
                        Log("Skipping Passworded Report {0}", item.Title);
                    }
                }
            }

            Log(Environment.NewLine + "Download Completed. Total of {0} reports found", reports.Count);
            return reports;
        }

        private static string GetEpisodeDir(string showName, int seasonNumber, int episodeNumber)
        {
            showName = CleanString(showName);

            string snReplace = showName;
            string sDotNReplace = showName.Replace(' ', '.');
            string sUnderNReplace = showName.Replace(' ', '_');

            string zeroSReplace = String.Format("{0:00}", seasonNumber);
            string sReplace = Convert.ToString(seasonNumber);
            string zeroEReplace = String.Format("{0:00}", episodeNumber);
            string eReplace = Convert.ToString(episodeNumber);

            string path = Path.GetDirectoryName(_tvRoot + "\\" + _tvTemplate);


            path = path.Replace(".%ext", "");
            path = path.Replace("%sn", snReplace);
            path = path.Replace("%s.n", sDotNReplace);
            path = path.Replace("%s_n", sUnderNReplace);
            path = path.Replace("%0s", zeroSReplace);
            path = path.Replace("%s", sReplace);
            path = path.Replace("%0e", zeroEReplace);
            path = path.Replace("%e", eReplace);

            return path;
        }

        private static string GetEpisodeFileMask(int seasonNumber, int episodeNumber)
        {
            string zeroSReplace = String.Format("{0:00}", seasonNumber);
            string sReplace = Convert.ToString(seasonNumber);
            string zeroEReplace = String.Format("{0:00}", episodeNumber);
            string eReplace = Convert.ToString(episodeNumber);

            string fileName = Path.GetFileName(_tvRoot + "\\" + _tvTemplate);

            fileName = fileName.Replace(".%ext", "");
            fileName = fileName.Replace("%en", "*");
            fileName = fileName.Replace("%e.n", "*");
            fileName = fileName.Replace("%e_n", "*");
            fileName = fileName.Replace("%sn", "*");
            fileName = fileName.Replace("%s.n", "*");
            fileName = fileName.Replace("%s_n", "*");
            fileName = fileName.Replace("%0s", zeroSReplace);
            fileName = fileName.Replace("%s", sReplace);
            fileName = fileName.Replace("%0e", zeroEReplace);
            fileName = fileName.Replace("%e", eReplace);

            return fileName;
        }

        private static string GetEpisodeDir(string showName, int year, int month, int day)
        {
            string path = Path.GetDirectoryName(_tvRoot + "\\" + _tvDailyTemplate);

            showName = CleanString(showName);

            string tReplace = showName;
            string dotTReplace = showName.Replace(' ', '.');
            string underTReplace = showName.Replace(' ', '_');
            string yearReplace = Convert.ToString(year);
            string zeroMReplace = String.Format("{0:00}", month);
            string mReplace = Convert.ToString(month);
            string zeroDReplace = String.Format("{0:00}", day);
            string dReplace = Convert.ToString(day);

            path = path.Replace(".%ext", "");
            path = path.Replace("%t", tReplace);
            path = path.Replace("%.t", dotTReplace);
            path = path.Replace("%_t", underTReplace);
            path = path.Replace("%y", yearReplace);
            path = path.Replace("%0m", zeroMReplace);
            path = path.Replace("%m", mReplace);
            path = path.Replace("%0d", zeroDReplace);
            path = path.Replace("%d", dReplace);

            return path;
        } //Ends GetDailyShowNamingScheme

        private static string GetEpisodeFileMask(int year, int month, int day)
        {
            string fileMask = Path.GetFileName(_tvRoot + "\\" + _tvDailyTemplate);

            string yearReplace = Convert.ToString(year);
            string zeroMReplace = String.Format("{0:00}", month);
            string mReplace = Convert.ToString(month);
            string zeroDReplace = String.Format("{0:00}", day);
            string dReplace = Convert.ToString(day);

            fileMask = fileMask.Replace(".%ext", "*");
            fileMask = fileMask.Replace("%desc", "*");
            fileMask = fileMask.Replace("%.desc", "*");
            fileMask = fileMask.Replace("%_desc", "*");
            fileMask = fileMask.Replace("%t", "*");
            fileMask = fileMask.Replace("%.t", "*");
            fileMask = fileMask.Replace("%_t", "*");
            fileMask = fileMask.Replace("%y", yearReplace);
            fileMask = fileMask.Replace("%0m", zeroMReplace);
            fileMask = fileMask.Replace("%m", mReplace);
            fileMask = fileMask.Replace("%0d", zeroDReplace);
            fileMask = fileMask.Replace("%d", dReplace);

            return fileMask;
        } //Ends GetDailyShowNamingScheme

        private static string CleanString(string name)
        {
            string result = name;
            string[] badCharacters = { "\\", "/", "<", ">", "?", "*", ":", "|", "\"" };
            string[] goodCharacters = { "+", "+", "{", "}", "!", "@", "-", "#", "`" };


            for (int i = 0; i < badCharacters.Length; i++)
            {
                if (_sabReplaceChars)
                {
                    result = result.Replace(badCharacters[i], goodCharacters[i]);
                }
                else
                {
                    result = result.Replace(badCharacters[i], "");
                }
            }

            return result.Trim();
        }

        private static bool IsShowWanted(string wantedShowName)
        {
            foreach (var di in _wantedShowNames)
            {
                if (String.Equals(di.Name, CleanString(wantedShowName), StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }
            Log("'{0}' is not being watched.", wantedShowName);
            return false;
        } //Ends IsShowWanted

        private static bool IsEpisodeWanted(string title, Int64 reportId)
        {
            Log("----------------------------------------------------------------");
            Log("Verifying '{0}'", title);

            try
            {


                if (title.Length > 80)
                {
                    title = title.Substring(0, 79);
                }

                string[] titleArray = title.Split('-');

                if (titleArray.Length == 3)
                {
                    string showName = titleArray[0].Trim();
                    string seasonEpisode = titleArray[1].Trim();

                    string[] seasonEpisodeSplit = seasonEpisode.Split('x');
                    int seasonNumber;
                    int episodeNumber;

                    Int32.TryParse(seasonEpisodeSplit[0], out seasonNumber);
                    Int32.TryParse(seasonEpisodeSplit[1], out episodeNumber);


                    // Go through each video file extension
                    if (!IsShowWanted(showName))
                        return false;

                    string dir = GetEpisodeDir(showName, seasonNumber, episodeNumber);
                    string fileMask = GetEpisodeFileMask(seasonNumber, episodeNumber);
                    if (IsOnDisk(dir, fileMask))
                        return false;

                    if (IsSeasonIgnored(showName, seasonNumber))
                        return false;

                    if (IsInQueue(title, reportId))
                        return false;

                    if (InNzbArchive(title))
                        return false;

                    return true;
                }

                if (titleArray.Length == 4)
                {
                    string showName;
                    string seasonEpisode;


                    if (titleArray[1].Contains("x"))
                    {
                        showName = titleArray[0].Trim();
                        seasonEpisode = titleArray[1].Trim();
                    }

                    else if (titleArray[2].Contains("x"))
                    {
                        showName = titleArray[0].Trim() + titleArray[1].Trim();
                        seasonEpisode = titleArray[2].Trim();
                    }

                    else
                    {
                        Log("Unsupported Title: {0}", title);
                        return false;
                    }

                    string[] seasonEpisodeSplit = seasonEpisode.Split('x');
                    int seasonNumber;
                    int episodeNumber;

                    Int32.TryParse(seasonEpisodeSplit[0], out seasonNumber);
                    Int32.TryParse(seasonEpisodeSplit[1], out episodeNumber);

                    if (!IsShowWanted(showName))
                        return false;

                    string dir = GetEpisodeDir(showName, seasonNumber, episodeNumber);
                    string fileMask = GetEpisodeFileMask(seasonNumber, episodeNumber);
                    if (IsOnDisk(dir, fileMask))
                        return false;

                    if (IsSeasonIgnored(showName, seasonNumber))
                        return false;

                    if (IsInQueue(title, reportId))
                        return false;

                    if (InNzbArchive(title))
                        return false;

                    return true;
                }


                //Daiy Episode
                if (titleArray.Length == 5)
                {
                    string showName = titleArray[0].Trim();
                    int year;
                    int month;
                    int day;

                    Int32.TryParse(titleArray[1], out year);
                    Int32.TryParse(titleArray[2], out month);
                    Int32.TryParse(titleArray[3], out day);

                    if (!IsShowWanted(showName))
                        return false;

                    string dir = GetEpisodeDir(showName, year, month, day);
                    string fileMask = GetEpisodeFileMask(year, month, day);
                    if (IsOnDisk(dir, fileMask))
                        return false;

                    if (IsInQueue(title, reportId))
                        return false;

                    if (InNzbArchive(title))
                        return false;

                    return true;
                }
            }
            catch (Exception e)
            {
                Log("Unsupported Title: {0} - {1}", title, e);
                return false;
            }

            Log("Unsupported Title: {0}", title);
            return false;
        }

        private static bool IsOnDisk(string dir, string fileMask)
        {
            if (!Directory.Exists(dir))
                return false;

            foreach (var ext in _videoExt)
            {
                var matchingFiles = Directory.GetFiles(dir, fileMask + ext);

                if (matchingFiles.Length != 0)
                {
                    Log("Episode in disk. '{0}'", true, matchingFiles[0]);
                    return true;
                }
            }
            return false;
        }

        private static bool IsSeasonIgnored(string showName, int seasonNumber)
        {
            if (_ignoreSeasons.Contains(showName))
            {
                string[] showsSeasonIgnore = _ignoreSeasons.Trim(';', ' ').Split(';');
                foreach (string showSeasonIgnore in showsSeasonIgnore)
                {
                    string[] showNameIgnoreSplit = showSeasonIgnore.Split('=');
                    string showNameIgnore = showNameIgnoreSplit[0];
                    int seasonIgnore = Convert.ToInt32(showNameIgnoreSplit[1]);

                    if (showNameIgnore == showName)
                    {
                        if (seasonNumber <= seasonIgnore)
                        {
                            Log("Ignoring '{0}' Season '{1}'  ", showName, seasonNumber);
                            return true;
                        } //End if seasonNumber Less than or Equal to seasonIgnore
                    } //Ends if showNameIgnore equals showName
                } //Ends foreach loop for showsSeasonIgnore
            } //Ends if _ignoreSeasons contains showName
            return false; //If Show Name is not being ignored or that season is not ignored return false
        } //Ends IsSeasonIgnored

        private static bool IsInQueue(string rssTitle, Int64 reportId)
        {
            try
            {
                string queueRssUrl = String.Format(_sabRequest, "mode=queue&output=xml");
                string fetchName = String.Format("fetching msgid {0} from www.newzbin.com", reportId);

                XmlTextReader queueRssReader = new XmlTextReader(queueRssUrl);
                XmlDocument queueRssDoc = new XmlDocument();
                queueRssDoc.Load(queueRssReader);


                var queue = queueRssDoc.GetElementsByTagName(@"queue");
                var error = queueRssDoc.GetElementsByTagName(@"error");
                if (error.Count != 0)
                {
                    Log("Sab Queue Error: {0}", true, error[0].InnerText);
                }

                else if (queue.Count != 0)
                {
                    var slot = ((XmlElement)queue[0]).GetElementsByTagName("slot");

                    foreach (var s in slot)
                    {
                        XmlElement queueElement = (XmlElement)s;

                        //Queue is empty
                        if (String.IsNullOrEmpty(queueElement.InnerText))
                            return false;

                        string fileName = queueElement.GetElementsByTagName("filename")[0].InnerText.ToLower();


                        if (fileName.ToLower() == CleanString(rssTitle).ToLower() || fileName == fetchName)
                        {
                            Log("Episode in queue - '{0}'", true, rssTitle);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("An Error has occurred while checking the queue. {0}", true, ex);
            }

            return false;
        } //Ends IsInQueue

        private static bool InNzbArchive(string rssTitle)
        {
            Log("Checking for Imported NZB for [{0}]", rssTitle);
            //return !File.Exists(_nzbDir + "\\" + rssTitle + ".nzb.gz");

            string nzbFileName = rssTitle.TrimEnd('.');
            nzbFileName = CleanString(nzbFileName);

            if (File.Exists(_nzbDir + "\\" + nzbFileName + ".nzb.gz"))
            {
                Log("Episode in archive: " + nzbFileName + ".nzb.gz", true);
                return true;
            }

            return false;
        }

        private static string AddToQueue(Int64 reportId)
        {
            string nzbFileDownload = String.Format(_sabRequest, "mode=addid&name=" + reportId);
            Log("Adding report [{0}] to the queue.", reportId);
            WebClient client = new WebClient();
            string response = client.DownloadString(nzbFileDownload).Replace("\n", String.Empty);
            Log("Queue Response: [{0}]", response);
            return response;
        } // Ends AddToQueue

        private static void Log(string message)
        {
            Console.WriteLine(message);
            try
            {
                using (StreamWriter sw = File.AppendText(LogFile.FullName))
                {
                    sw.WriteLine(message);
                }
            }
            catch { }
        }

        private static void Log(string message, params object[] para)
        {

            Log(String.Format(message, para));
        }

        private static void Log(string message, bool showInSummary)
        {
            if (showInSummary) Summary.Add(message);
            Log(message);
        }

        private static void Log(string message, bool showInSummary, params object[] para)
        {
            Log(String.Format(message, para), showInSummary);
        }
    }
}