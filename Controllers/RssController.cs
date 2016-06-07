using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Net.Http;
using Newtonsoft.Json;

namespace rss_expander.Controllers
{
    class ReadabilityResponse {
        public string content { get; set; }
    }

    public class RssController : Controller
    {
        public async Task<HttpContent> FetchUrl(string url)
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(url))
            {
                return response.Content;
            }
        }

        public async Task<string> Readable(string url)
        {
            var encoded = Uri.EscapeUriString(url);
            var parser = $"http://readability.com/api/content/v1/parser?url=${encoded}&token=dbd37a7736b9e617bed66666c7d2a806395d4e83"
            using (var content = await FetchUrl(parser))
            {
                var json = JsonConvert.DeserializeObject<ReadabilityResponse>(await content.ReadAsStringAsync());

                return json.content;
            }
        }

        // GET api/values
        [HttpGet, Route("/expanded-rss")]
        public async Task<IActionResult> Get(string url)
        {
            using (var content = await FetchUrl(url))
            {
                var doc = XDocument.Load(await content.ReadAsStreamAsync());

                var items = doc.Root.
                    Descendants().
                    First(x => x.Name.LocalName == "channel").
                    Descendants().
                    Where(x => x.Name.LocalName == "item");

                foreach (var item in items)
                {
                    var link = item.Descendants().
                        Where(x => x.Name.LocalName == "link").
                        First().
                        Value;

                    var descNode = item.Descendants().
                        Where(x => x.Name.LocalName == "description").
                        First();

                    descNode.ReplaceNodes(new XCData(await Readable(link)));
                }

                
                
                return Ok(doc.Declaration.ToString() + doc.ToString());
            }
        }
    }
}
