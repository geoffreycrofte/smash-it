# Accessibility

Smash It should work for everyone, whether you use a mouse, a keyboard or a screen reader. Here is what works
today, what we haven't checked yet, and how to tell us when something gets in your way.

## What works today

**Built with standard Windows controls.** Buttons, sliders, switches and lists are the ones Windows provides, so
they work with the keyboard, Narrator and other screen readers the way you expect.

**No mouse needed.**
- Use Tab and Shift+Tab to move around, then Space or Enter to press a button.
- No need to drag and drop: **Add** opens a file picker, and "Compress with Smash It" works from the File Explorer
  right-click menu (or the menu key, or Shift+F10).
- In the file list, the arrow keys move from file to file, and Enter opens the before/after view.

**Every control has a name.** Buttons that only show an icon (Settings, Clear the list, Stop, Compare, 100 %)
have a name that screen readers read out, and a tooltip. Settings that look alike have names that tell them apart
("Image quality", "PDF image quality", "Video quality"). Decorative pictures, like thumbnails, are skipped.

**Each file is read as one sentence.** A row in the list reads like "photo.jpg, 2.3 MB → 1.1 MB, −52 %", or
"photo.jpg, Waiting" while it waits, so you hear the result without going through every column.

**Progress is announced.** The line at the bottom of the window ("3 being compressed, 12 waiting", then the space
saved) is read out by screen readers as it changes, so you know when a batch is done without looking.

**The before/after view works from the keyboard.**
- ← and → move the divider. Hold Shift for smaller steps.
- Home and End show only the "after" or only the "before" image.
- Z switches between the whole image and 100 % (pixel for pixel).
- Esc closes the view.

**The divider stays visible.** Its handle is a white circle with dark arrows, on purpose: it stands out on any
picture, in light, dark or high contrast mode.

**Each file's status is written out.** The list says "Waiting", "Compressing…", "−42 %", "Already optimal" or
"Not supported" in words, not only with an icon or a color.

**Presets in plain words.** "Best quality", "Web", "Email" and "Archive" each come with a sentence that says what
they do, so you don't need to know what lossy compression or dpi mean.

**Follows your Windows look.** Light or dark mode comes from your Windows settings.

**Your language.** Smash It is available in English and French, and follows the Windows language unless you
choose one in Settings.

## How we check

The main window, the settings and the before/after view have been checked with a pass over what screen readers see
(Windows UI Automation): every button, list, slider and link has a name, and no icon is read out as a strange
character. The keyboard paths above have been tried for real. This check isn't automatic yet: it's redone by hand
when the interface changes.

## What we haven't checked yet

We'd rather be upfront about these:

- **High contrast themes**: the app uses the colors Windows provides, so it should follow them, but we haven't
  looked at every screen with one turned on.
- **Large text** (Windows "Text size" setting): some columns of the file list have a fixed width and may cut long
  text short.
- **Listening with Narrator from start to finish**: the automated pass says everything has a name, but nothing
  replaces hearing it, especially while a batch runs.

## Something doesn't work for you?

Please tell us. [Open an issue on GitHub](https://github.com/geoffreycrofte/smash-it/issues) and describe what you
were trying to do and what got in the way. Mention the assistive technology you use, if any (Narrator, NVDA,
JAWS, magnifier, voice control…). Accessibility problems are treated as bugs, not wishes.
