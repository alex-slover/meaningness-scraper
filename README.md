# Meaningness EPUB converter

A quick tool to turn David Chapman's [Meaningness](https://meaningness.com) into a read-anywhere* EPUB ebook.

\* **Note to Kindle users:** This tool only produces an EPUB file, which by itself will not work on a Kindle. You'll have to convert it to a MOBI file with [Calibre](https://calibre-ebook.com). Note that the Table of Contents does not work after MOBI conversion, since as far as I can tell MOBI TOCs can only be 3 levels deep, and Meaningness' TOC is 9.

## How to run

1. Clone the project
2. Install .NET Core on your platform of choice (instructions for [Windows](https://www.microsoft.com/net/core#windowscmd), [Mac](https://www.microsoft.com/net/core#macos), and [Linux](https://www.microsoft.com/net/core#linuxredhat))
3. Build the project
```command
dotnet build
```
4. Run the project
```command
dotnet run -- -o Meaningness.epub
```

## Note on slowness

You'll notice the download process is a bit slow (about 30 minutes for all 183 chapters). This is because the script honors the Crawl-Delay in Meaningness' robots.txt file and only downloads one file every 10 seconds. You could change this, but consider whether you are being a good web citizen by so doing.

If a run is interrupted, you can resume it where it left off with the `-d chapterFiles/` option, using the folder from the last run.
