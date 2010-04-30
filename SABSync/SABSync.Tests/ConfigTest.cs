﻿using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using NUnit.Framework;

namespace SABSync.Tests
{
    [TestFixture]
    public class ConfigTest : AssertionHelper
    {
        private Config Config { get; set; }

        public ConfigTest()
        {
            Config = new Config(new NameValueCollection
            {
                {"tvroot", @"..\..\TV;..\..\TV2;"},
                {"rss", @"..\..\rss.fileuri.config"},
            });
        }

        [Test]
        public void Feeds()
        {
            IList<FeedInfo> feeds = new List<FeedInfo>
            {
                new FeedInfo {Name = "NzbMatrix", Url = @"..\..\Feed.nzbmatrix.com.xml"},
                new FeedInfo {Name = "Feed", Url = @"..\..\Feed.xml"},
            };

            Expect(Config.Feeds, Is.EquivalentTo(feeds));
        }


        [Test]
        public void TvRootFolders()
        {
            var folders = new List<DirectoryInfo>
            {
                new DirectoryInfo(@"..\..\TV"),
                new DirectoryInfo(@"..\..\TV2"),
            };

            Expect(Config.TvRootFolders, Is.EquivalentTo(folders));
        }
    }
}