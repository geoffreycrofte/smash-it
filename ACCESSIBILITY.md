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

**Buttons say what they do.** Buttons that only show an icon (Settings, Clear the list, Stop, Compare) have a name
that screen readers read out, and a tooltip.

**Progress is announced.** The line at the bottom of the window ("3 being compressed, 12 waiting", then the space
saved) is read out by screen readers as it changes, so you know when a batch is done without looking.

**The before/after preview works from the keyboard.**
- ← and → move the divider. Hold Shift for smaller steps.
- Home and End show only the "after" or only the "before" image.
- Esc closes the preview.

**Each file's status is written out.** The list says "Waiting", "Compressing…", "−42 %", "Already optimal" or
"Error" in words, not only with an icon or a color.

**Follows your Windows look.** Light or dark mode comes from your Windows settings.

**Your language.** Smash It is available in English and French, and follows the Windows language unless you
choose one in Settings.

## What we haven't checked yet

We'd rather be upfront about these:

- **High contrast themes**: most of the app should follow them, but we haven't tested every screen.
- **Large text** (Windows "Text size" setting): some parts of the window may cut long text short.
- **A full Narrator walk-through**: the main path works, but some details, like how the file list rows are read
  out, still need a careful pass.
- **The before/after divider**: its arrow icons are always black, which can be hard to see in some themes.

## Something doesn't work for you?

Please tell us. [Open an issue on GitHub](https://github.com/geoffreycrofte/smash-it/issues) and describe what you
were trying to do and what got in the way. Mention the assistive technology you use, if any (Narrator, NVDA,
JAWS, magnifier, voice control…). Accessibility problems are treated as bugs, not wishes.
