using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using HtmlAgilityPack;
using Mono.Options;

namespace MeaningnessScraper {
    public class Program {

        // Pickaxe symbol in the TOC indicating a particular chapter is not done
        private const string UnfinishedMarker = "\u2692";

        private const string MeaningnessBaseAddress = "https://meaningness.com";

        private static readonly XmlWriterSettings writerSettings = new XmlWriterSettings {
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
            Indent = true
        };

        static void Main(string[] args) {
            string tocFile = null;
            string chapterFilesPath = null;
            string outputFile = null;
            bool forceDownload = false;

            var options = new OptionSet {
                {"c|chapterFile=", "The table of contents at meaningness.com. Leave blank to redownload", toc => tocFile = toc },
                {"d|chapterDirectory=", "The directory to download chapters to. Any that already exist will be skipped", path => chapterFilesPath = path },
                {"f|forceDownload", "Force redownload of chapters that already exist", f => forceDownload = (f != null) },
                {"o|output=", "The name of the output file. Must end in .epub", output => outputFile = output }
            };

            var ignored = options.Parse(args);

            if (chapterFilesPath == null) {
                throw new ArgumentNullException("chapterDirectory", "Must supply a directory to download chapter files to");
            }

            if (outputFile == null || !outputFile.EndsWith(".epub")) {
                throw new ArgumentException("output is null or in wrong format");
            }
            
            if (File.Exists(outputFile)) {
                File.Delete(outputFile);
            }

            // Download chapter list or read existing one from file
            List<Chapter> chapters = GetChapterListAsync(tocFile, chapterFilesPath).Result;

            // Download each chapter file
            using (HttpClient client = GetHttpClient()) {
                DoForAllChapters(chapters, c => DownloadChapterAsync(c, client, chapterFilesPath, forceDownload).Wait());
            }

            // Cleanup chapter files
            DoForAllChapters(chapters, c => CleanChapterFile(c.GetFileName(chapterFilesPath)));

            // Write the four metadata files necessary for the EPUB format
            // see http://www.hxa.name/articles/content/epub-guide_hxa7241_2007.html for details

            string calibreTocFile = Path.Combine(chapterFilesPath, "toc.ncx");
            using (XmlWriter writer = XmlWriter.Create(File.OpenWrite(calibreTocFile), writerSettings)) {
                WriteCalibreNcxFile(chapters, tocFile, writer);
            }

            string calibreOpfFile = Path.Combine(chapterFilesPath, "content.opf");
            using (XmlWriter writer = XmlWriter.Create(File.OpenWrite(calibreOpfFile), writerSettings)) {
                WriteCalibreOpfFile(chapters, tocFile, writer);
            }

            string calibreMimetypeFile = Path.Combine(chapterFilesPath, "mimetype");
            File.WriteAllBytes(calibreMimetypeFile, Encoding.ASCII.GetBytes("application/epub+zip"));

            // container.xml goes in META-INF directory
            string metaInfDirPath = Path.Combine(chapterFilesPath, "META-INF");
            Directory.CreateDirectory(metaInfDirPath);
            string calibreContainerFile = Path.Combine(metaInfDirPath, "container.xml");
            using (XmlWriter writer = XmlWriter.Create(File.OpenWrite(calibreContainerFile), writerSettings)) {
                WriteCalibreContainerFile(writer);
            }

            // Now turn it into a ZIP archive (EPUB files are just ZIP archives with a different extension)
            // ZipFile.CreateFromDirectory() has all kinds of weird bugs, so we have to copy new-chapters/ to a temp location first
            // So make the ZIP archive in a temp location and then move it
            // string tempPath = Path.GetTempPath();
            // string newChaptersTempPath = Path.Combine(tempPath, chapterFilesPath);
            // Directory.CreateDirectory(newChaptersTempPath);
            // string tempZipLocation = Path.GetTempFileName();
            // if (File.Exists(tempZipLocation)) {
            //     File.Delete(tempZipLocation);
            // }
            // ZipFile.CreateFromDirectory(chapterFilesPath, tempZipLocation);
            // File.Move(tempZipLocation, outputFile);
        }

        /// <summary>
        /// Clean up the raw HTML from the website and turn it into plaintext for the EPUB file
        /// </summary>
        private static void CleanChapterFile(string filename) {
            HtmlDocument existing = new HtmlDocument(), modified = new HtmlDocument();
            
            existing.LoadHtml(File.ReadAllText(filename));

            // We want only the <h1> header and the body, which is all inside <article>
            var h1 = existing.DocumentNode.SelectSingleNode("//h1");
            var article = existing.DocumentNode.SelectSingleNode("//article");

            // Remove FB/Twitter/comment links
            var navBar = article.SelectSingleNode("//nav[@class='clearfix']");
            article.RemoveChild(navBar);

            // Add to the new document
            modified.DocumentNode.AppendChild(h1);
            modified.DocumentNode.AppendChild(article);

            // Modify in place
            var newFileName = filename + ".tmp";
            modified.Save(newFileName);
            File.Delete(filename);
            File.Move(newFileName, filename);
        }

        /// <summary>
        /// Write the <seealso href="http://www.hxa.name/articles/content/epub-guide_hxa7241_2007.html">container.xml file for EPUB metadata</seealso>
        /// </summary>
        private static void WriteCalibreContainerFile(XmlWriter writer) {
            writer.WriteStartDocument();
            writer.WriteStartElement("container", "urn:oasis:names:tc:opendocument:xmlns:container");
            writer.WriteAttributeString("version", "1.0");
            writer.WriteAttributeString("xmlns", "urn:oasis:names:tc:opendocument:xmlns:container");
            
            writer.WriteStartElement("rootfiles");

            writer.WriteStartElement("rootfile");
            writer.WriteAttributeString("full-path", "content.opf");
            writer.WriteAttributeString("mediatype", "application/oebps-package+xml");
            writer.WriteEndElement(); // </rootfile>

            writer.WriteEndElement(); // </rootfiles>

            writer.WriteEndElement(); // </container>

            writer.WriteEndDocument();
        }

        /// <summary>
        /// Write the <seealso href="http://www.hxa.name/articles/content/epub-guide_hxa7241_2007.html">content.opf file for EPUB metadata</seealso>
        /// </summary>
        private static void WriteCalibreOpfFile(List<Chapter> chapters, string tocPath, XmlWriter writer) {
            writer.WriteStartDocument();

            writer.WriteStartElement("package");
            writer.WriteAttributeString("xmlmns", "http://www.idpf.org/2007/opf");
            writer.WriteAttributeString("unique-identifier", "dcidid");
            writer.WriteAttributeString("version", "2.0");

            writer.WriteStartElement("metadata");
            writer.WriteAttributeString("xmlns", "dc", null, "http://purl.org/dc/elements/1.1/");
            writer.WriteAttributeString("xmlns", "dcterms", null, "http://purl.org/dc/terms/");
            writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
            writer.WriteAttributeString("xmlns", "opf", null, "http://www.idpf.org/2007/opf");

            writer.WriteElementString("dc", "title", null, "Meaningness");
            
            writer.WriteStartElement("dc", "language", null);
            writer.WriteAttributeString("xsi", "type", null, "dcterms:RFC3066");
            writer.WriteString("en");
            writer.WriteEndElement(); // </dc:language>

            writer.WriteStartElement("dc", "identifier", null);
            writer.WriteAttributeString("id", "dcidid");
            writer.WriteAttributeString("opf", "scheme", null, "URI");
            writer.WriteString("https://meaningness.com");
            writer.WriteEndElement(); // </dc:identifier>

            writer.WriteElementString("dc", "description", null, "Better ways of thinking, feeling, and acting—around problems of meaning and meaninglessness; self and society; ethics, purpose, and value.");
            writer.WriteElementString("dc", "creator", null, "David Chapman");
            writer.WriteElementString("dc", "rights", null, $"Copyright ©2010–{DateTime.Now.Year} David Chapman.");

            writer.WriteEndElement(); // </metadata>

            writer.WriteStartElement("manifest");

            writer.WriteStartElement("item");
            writer.WriteAttributeString("id", "ncx");
            writer.WriteAttributeString("href", "toc.ncx");
            writer.WriteAttributeString("media-type", "application/x-dtbncx+xml");
            writer.WriteEndElement(); // </item>

            writer.WriteStartElement("item");
            writer.WriteAttributeString("id", "contents");
            writer.WriteAttributeString("href", tocPath);
            writer.WriteAttributeString("media-type", "application/x-dtbncx+xml");
            writer.WriteEndElement(); // </item>

            int index = 1;

            void WriteOpfChapterManifest(Chapter chapter) {
                writer.WriteStartElement("item");
                writer.WriteAttributeString("id", $"part{index}");
                writer.WriteAttributeString("href", chapter.LocalFilePath);
                writer.WriteAttributeString("media-type", "application/xhtml+xml");
                writer.WriteEndElement(); // </item>

                index++;
            }

            DoForAllChapters(chapters, WriteOpfChapterManifest);

            writer.WriteEndElement(); // </manifest>

            writer.WriteStartElement("spine");
            writer.WriteAttributeString("toc", "ncx");
            index = 1;

            writer.WriteStartElement("itemref");
            writer.WriteAttributeString("idref", "contents");
            writer.WriteEndElement(); // </itemref>

            void WriteOpfSpine(Chapter chapter) {
                writer.WriteStartElement("itemref");
                writer.WriteAttributeString("idref", $"part{index}");
                writer.WriteEndElement(); // </itemref>

                index++;
            }

            DoForAllChapters(chapters, WriteOpfSpine);

            writer.WriteEndElement(); // </spine>

            // guide

            writer.WriteEndElement(); // </package>

        }

        /// <summary>
        /// Write the <seealso href="http://www.hxa.name/articles/content/epub-guide_hxa7241_2007.html">toc.ncx file for EPUB metadata</seealso>
        /// </summary>
        private static void WriteCalibreNcxFile(List<Chapter> chapters, string tocPath, XmlWriter writer) {
            writer.WriteStartDocument();

            writer.WriteStartElement("ncx", "http://www.daisy.org/z3986/2005/ncx/");
            writer.WriteAttributeString("version", "2005-1");

            writer.WriteStartElement("head");

            writer.WriteStartElement("meta");
            writer.WriteAttributeString("name", "dtb:uid");
            writer.WriteAttributeString("content", "dcidid");
            writer.WriteEndElement(); // </meta>

            writer.WriteStartElement("meta");
            writer.WriteAttributeString("name", "dtb:depth");
            writer.WriteAttributeString("content", MaxDepth(chapters).ToString());
            writer.WriteEndElement(); // </meta>

            writer.WriteStartElement("meta");
            writer.WriteAttributeString("name", "dtb:totalPageCount");
            writer.WriteAttributeString("content", "0");
            writer.WriteEndElement(); // </meta>

            writer.WriteStartElement("meta");
            writer.WriteAttributeString("name", "dtb:maxPageNumber");
            writer.WriteAttributeString("content", "0");
            writer.WriteEndElement(); // </meta>

            writer.WriteStartElement("docTitle");
            writer.WriteElementString("text", "Meaningness");
            writer.WriteEndElement();

            writer.WriteStartElement("navMap");

            int playOrder = 1;

            writer.WriteStartElement("navPoint");
            writer.WriteAttributeString("id", "contents");
            writer.WriteAttributeString("playOrder", playOrder.ToString());

            writer.WriteStartElement("navLabel");
            writer.WriteElementString("text", "Table of Contents");
            writer.WriteEndElement();

            writer.WriteStartElement("content");
            writer.WriteAttributeString("src", tocPath);
            writer.WriteEndElement();

            playOrder++;

            void WriteCalibreNcxChapter(Chapter chapter) {
                writer.WriteStartElement("navPoint");
                writer.WriteAttributeString("id", $"navPoint-{playOrder}");
                writer.WriteAttributeString("playOrder", playOrder.ToString());

                writer.WriteStartElement("navLabel");
                writer.WriteElementString("text", chapter.Title);
                writer.WriteEndElement();

                writer.WriteStartElement("content");
                writer.WriteAttributeString("src", chapter.LocalFilePath);
                writer.WriteEndElement();

                playOrder++;
            }

            DoForAllChapters(chapters, WriteCalibreNcxChapter, c => writer.WriteEndElement());

            writer.WriteEndElement(); // navMap
            writer.WriteEndElement(); // ncx
            writer.WriteEndDocument();
        }

        /// <summary>
        /// Download the chapter list from https://meaningness.com if it doesn't already exist, and scan it to build the TOC
        /// </summary>
        /// <param name="tocFile">Name of TOC file if already exists, or where to download it to if not</param>
        /// <param name="chapterFilesDir">Directory to place the resulting TOC and HTML files in</param>
        private async static Task<List<Chapter>> GetChapterListAsync(string tocFile, string chapterFilesDir) {
            bool usingTempFile = false;

            if (tocFile == null) {
                // No existing TOC specified, redownload
                Console.Write("Downloading table of contents from meaningness.com... ");

                tocFile = System.IO.Path.GetTempFileName();
                usingTempFile = true;

                using (HttpClient client = new HttpClient { BaseAddress = new Uri(MeaningnessBaseAddress) }) 
                using (Stream httpStream = await client.GetStreamAsync("/")) 
                using (FileStream fileStream = File.OpenWrite(tocFile)) {
                    await httpStream.CopyToAsync(fileStream);
                }
                
                Console.Write("done!\n");
            }

            // tocFile should now contain chapter list
            var document = new HtmlDocument();
            document.LoadHtml(await File.ReadAllTextAsync(tocFile));

            // Start recursive chapter scan from top-level element
            XPathNodeIterator iter = document.CreateNavigator().Select("//ul[@class='book-toc']/li");
            List<Chapter> result = ReadChildChapters(iter);

            // Cleanup temp files
            if (usingTempFile) {
                File.Copy(tocFile, Path.Combine(chapterFilesDir, "meaningness.html"));
                File.Delete(tocFile);
            }

            return result;
        }

        /// <summary>
        /// Download a chapter
        /// </summary>
        /// <param name="chapters">chapter object</param>
        /// <param name="client">client for making downloads. should be initialized with the right parameters</param>
        /// <param name="chapterFilesDir">directory to download chapter to</param>
        /// <param name="forceDownload">if true, download again even if it already exists</param>
        private async static Task DownloadChapterAsync(Chapter chapter, HttpClient client, string chapterFilesDir, bool forceDownload) {
            string path = chapter.GetFileName(chapterFilesDir);

            if (!File.Exists(path) || forceDownload) {
                if (File.Exists(path)) {
                    File.Delete(path);
                }

                Console.Write($"Currently downloading \"{chapter.Title}\" ..... ");

                using (Stream httpStream = await client.GetStreamAsync(chapter.Url))
                using (FileStream fileStream = File.OpenWrite(path)) {
                    await httpStream.CopyToAsync(fileStream);
                }

                Console.Write("done!\n");

                Thread.Sleep(1000 * 10); // honor Crawl-Delay in robots.txt
            }
        }

        /// <summary>
        /// Recursive method for reading the sub-chapters of a given chapter
        /// </summary>
        /// <param name="iter">iterator at the current level</param>
        private static List<Chapter> ReadChildChapters(XPathNodeIterator iter) {
            List<Chapter> result = new List<Chapter>();

            XPathNavigator current, anchor;
            Chapter parent = null;

            while (iter.MoveNext()) {
                string liClass = iter.Current.GetAttribute("class", null);

                if (liClass == null) {
                    // Parent node (chapter header) with no children

                    current = iter.Current.Clone();
                    anchor = current.SelectSingleNode("./a");

                    // Make a new top-level chapter market
                    string text = anchor.Value;
                    parent = new Chapter {
                        Title = text.Replace(UnfinishedMarker, "").Trim(),
                        Url = anchor.GetAttribute("href", null),
                        Unfinished = text.Contains(UnfinishedMarker)
                    };
                    result.Add(parent);
                } else if (liClass == "book_toc_container") {
                    // Select child nodes
                    parent.Children = ReadChildChapters(iter.Current.Clone().Select("./ul/li"));
                 }
            }

            return result;
        }

        /// <summary>
        /// Make a new HttpClient with the desired parameters
        /// </summary>
        private static HttpClient GetHttpClient() {
            HttpClient client = new HttpClient { BaseAddress = new Uri(MeaningnessBaseAddress) };
            client.DefaultRequestHeaders.Add("User-agent", "meaningness-scraper v0.8");
            return client;
        }

        /// <summary>
        /// Count the maximum depth of the chapter tree (necessary parameter for EPUB metadata format)
        /// </summary>
        private static int MaxDepth(List<Chapter> chapters) {
            if (chapters?.Count == 0) { return 0; }

            var depths = chapters.Select(c => MaxDepth(c.Children)).OrderByDescending(x => x);
            return 1 + depths.First();
        }

        /// <summary>
        /// Helper method to recursively perform an action for all chapters
        /// </summary>
        /// <param name="chapters">chapter list</param>
        /// <param name="action">action to perform. can be an implicit closure capturing outer variables</param>
        /// <param name="afterAction">Optional: action to perform after acting on children (like closing an HTML/XML tag)</param>
        static void DoForAllChapters(List<Chapter> chapters, Action<Chapter> action, Action<Chapter> afterAction = null) {
            foreach (Chapter c in chapters) {
                action(c);

                DoForAllChapters(c.Children, action, afterAction);
                
                if (afterAction != null) {
                    afterAction(c);
                }
            }
        }
    }

    class Chapter {
        public string Title { get; set; }

        public string Url { get; set; }

        public string LocalFilePath => $".{this.Url}.html";

        public bool Unfinished { get; set; }

        public List<Chapter> Children { get; set; } = new List<Chapter>();

        public string GetFileName(string dir) => Path.Combine(dir, this.Url.TrimStart('/') + ".html");
    }
}
