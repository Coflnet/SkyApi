using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller
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
            Assert.That(SearchController.EnoughResults("xx", 5));
            Assert.That(SearchController.EnoughResults("xxxxx", 2));
            Assert.That(SearchController.EnoughResults("xxxxxx", 1));
            Assert.That(!SearchController.EnoughResults("x", 9));
            Assert.That(!SearchController.EnoughResults("xxxx", 1));
            Assert.That(!SearchController.EnoughResults("xxxxxxxxxxxxxxx", 0), "no matter how long minimum is one");
        }
    }
}

