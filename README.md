# Meaningness EPUB converter

A quick tool to turn David Chapman's [Meaningness](https://meaningness.com) into a read-anywhere* EPUB ebook, in the manner of the existing [Worm](https://github.com/rhelsing/worm_scraper) and [Unsong](https://github.com/JasonGross/unsong_scraper) scrapers.

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
dotnet run -- -d chapterFiles/ -o Meaningness.epub
```

