# Changelog

What's new in each version of Smash It, newest first.

<!--
How this file works:
- The app shows these notes in "What's new", and each GitHub release uses its version's section.
- One section per version, titled "## vX.Y.Z" (a date can follow).
- People see every section above the version they have.
- "## Next version" is never shown: rename it to "## vX.Y.Z" when you tag the release.
- CHANGELOG.fr.md holds the French translation, section by section, shown when the app is in French.
-->

## Next version

### Improved
- **"What's new" in your language.** When Smash It is in French, the notes of each update now show in French too.

### Fixed
- Sizes just under a megabyte read "0.98 MB" instead of "1003.4 KB".

## v0.4.0 · November 19, 2026

### New
- **Presets.** Pick "Web", "Email" or "Archive" and every setting follows, for images, PDFs and videos alike. The list sits at the bottom of the window, next to the Settings button. Change any setting and the preset becomes "Custom".
- **Lighter videos from your phone.** Choose a maximum resolution, 1080p or 720p, and 4K videos are brought down to it: often 3 to 4 times smaller, still sharp on any screen. The Web preset uses 1080p, Email 720p.
- **Your photos keep their story.** With "Remove metadata" unticked, the date, camera and location of an iPhone HEIC, an AVIF or a WebP now follow it when you convert it.
- **Zoom to 100 % in the before/after view.** Press "100 %" (or Z) to see every pixel and judge a lossy compression up close. Press it again to see the whole image.

### Improved
- **Better with a screen reader.** Each file in the list is read as one sentence ("photo.jpg, 2.3 MB → 1.1 MB, −52 %"), every button has a name, and settings that looked alike now have names that tell them apart.
- **Enter opens the before/after view** of the selected file, like a double-click.
- **A tidier empty window.** "Add files" now sits right under the drop area. The list buttons (Add, Recompress, Clear) and the column titles only show once there are files.
- **The Tab key follows the screen**: the files, then the settings when they're open, then the bottom bar.
- **Larger text, nothing cut off.** If you've made text bigger in Windows, the file list columns and the settings panel now grow with it.

### Fixed
- WebP files made by Smash It now keep the date, camera and location when you choose to keep metadata. Until now, only the color profile made it.

## v0.3.0 · November 19, 2026

### New
- **Smaller GIFs, animations included.** Smash It now compresses GIFs while keeping every frame and the looping. In lossy mode, they often end up much lighter.
- **Cleaner, lighter SVGs.** Smash It removes what design tools leave behind in SVG files and writes them more compactly. They look exactly the same.
- **Much lighter scanned PDFs.** In lossy mode, images far more detailed than their size on the page are brought down to 150 dpi (screen) or 300 dpi (print), your choice. A scanned document often ends up 5 to 10 times smaller, and still reads just as well.

### Improved
- Files Smash It can't work on, like a signed PDF, now say "Not supported" with the reason, instead of "Error".

### Fixed
- In "What's new", long items no longer break into several lines.

## v0.2.0 · November 19, 2026

### New
- **Right-click to compress.** Select images, PDFs, videos or whole folders in File Explorer and choose "Compress with Smash It". On Windows 11, it's under "Show more options". Turn it on or off in Settings.
- **Keep your originals tidy.** When you compress several files, choose where the smaller copies go: next to the originals, or together in a "compressed" folder. Smash It remembers your choice.
- **Get the whole set in one place.** With the "compressed" folder, files that were already as small as they can get can be copied there too.
- **Much smaller PNGs** in lossy mode.
- **Updates in one click.** Smash It tells you when a new version is out, shows what's new, and updates itself.
- **Your settings stay put** from one launch to the next.

### Improved
- The bottom bar shows how many files are being compressed and how many are waiting.
- A **Stop** button ends a batch. Files not yet compressed stay as they are.
- Settings now has an About section, with the open source projects Smash It is built on.

## v0.1.0 · November 19, 2026

The first public version.

- Make images (JPEG, PNG, WebP, AVIF, HEIC, TIFF, GIF), PDFs and videos smaller, with or without quality loss.
- Convert images to another format.
- Compare before and after for every image.
- Your originals are kept unless you choose otherwise.
- Available in English and French.
