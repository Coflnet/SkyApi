using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Tests for <see cref="SearchController"/>
    /// </summary>
    public class ApiControllerTests
    {
        /// <summary>
        /// Test search result threshold
        /// </summary>
        [Test]
        public void MinResultCount()
        {
            Assert.IsTrue(SearchController.EnoughResults("xx", 5));
            Assert.IsTrue(SearchController.EnoughResults("xxxxx", 2));
            Assert.IsTrue(SearchController.EnoughResults("xxxxxx", 1));
            Assert.IsFalse(SearchController.EnoughResults("x", 9));
            Assert.IsFalse(SearchController.EnoughResults("xxxx", 1));
            Assert.IsFalse(SearchController.EnoughResults("xxxxxxxxxxxxxxx", 0), "no matter how long minimum is one");
        }
    }
}

