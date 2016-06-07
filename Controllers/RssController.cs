using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace rss_expander.Controllers
{
    class ReadabilityResponse
    {
        public string content { get; set; }
    }

    public class RssController : Controller
    {
        static HttpClient HttpClient { get; set; } = new HttpClient();

        IDatabase redis { get; }

        public RssController(RedisDb db)
        {
            this.redis = db.db;
        }

        public async Task<HttpContent> FetchUrl(string url)
        {
            var response = await HttpClient.GetAsync(url);
            return response.Content;
        }

        string CacheKey(string url)
        {
            return "rss_expand_" + url;
        }

        public async Task<string> Readable(string url)
        {
            var read = await redis.StringGetAsync(CacheKey(url));

            if (read.HasValue)
            {
                return read.ToString();
            }

            var encoded = Uri.EscapeUriString(url);
            var parser = $"http://readability.com/api/content/v1/parser?url={encoded}&token=dbd37a7736b9e617bed66666c7d2a806395d4e83";
            using (var content = await FetchUrl(parser))
            {
                var json = JsonConvert.DeserializeObject<ReadabilityResponse>(await content.ReadAsStringAsync());

                redis.StringSet(CacheKey(url), json.content);

                return json.content;
            }
        }

        // GET api/values
        [HttpGet, Route("/expanded-rss")]
        public async Task<IActionResult> Get(string url)
        {
            using (var response = await HttpClient.GetAsync(url))
            using (var content = response.Content)
            {
                var doc = XDocument.Load(await content.ReadAsStreamAsync());

                var isRSS = doc.Root.Name.LocalName == "rss";

                HttpContext.Response.ContentType = "text/xml";

                if (isRSS)
                {
                    return Ok(await processRSS(doc));
                }

                return Ok(await processAtom(doc));
            }
        }

        async Task<string> processAtom(XDocument doc)
        {
            var entries = doc.Root
                .Descendants()
                .Where(x => x.Name.LocalName == "entry");

            foreach (var entry in entries)
            {
                var link = entry.Descendants()
                    .Where(x => x.Name.LocalName == "link")
                    .First()
                    .Attribute("href")
                    .Value;

                var contentNode = entry.Descendants()
                    .Where(x => x.Name.LocalName == "content")
                    .First();

                var guidNode = entry.Descendants()
                    .Where(x => x.Name.LocalName == "id")
                    .First();

                guidNode.SetValue(guidNode.Value + "-full");

                var newContent = await Readable(link);

                contentNode.ReplaceAll(new XCData(contentNode.Value + "<hr/>" + newContent));
            }

            return doc.Declaration.ToString() + doc.ToString();
        }

        async Task<String> processRSS(XDocument doc)
        {
            var items = doc.Root
                .Descendants()
                .First(x => x.Name.LocalName == "channel")
                .Descendants()
                .Where(x => x.Name.LocalName == "item");

            foreach (var item in items)
            {
                var link = item.Descendants()
                    .Where(x => x.Name.LocalName == "link")
                    .First()
                    .Value;

                var descNode = item.Descendants()
                    .Where(x => x.Name.LocalName == "description")
                    .First();

                var guidNode = item.Descendants()
                    .Where(x => x.Name.LocalName == "guid")
                    .First();

                guidNode.SetValue(guidNode.Value + "-full");

                var newContent = await Readable(link);

                descNode.ReplaceAll(new XCData(descNode.Value + "<hr/>" + newContent));
            }

            return doc.Declaration.ToString() + doc.ToString();
        }
    }
}
