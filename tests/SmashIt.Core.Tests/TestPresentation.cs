using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace SmashIt.Core.Tests;

/// <summary>Writes real PowerPoint files with the Open XML SDK: a master, a layout, a theme, and one picture per slide.</summary>
internal static class TestPresentation
{
    /// <summary>EMUs per inch.</summary>
    public const long Inch = 914_400;

    /// <summary>A picture to place on its own slide, at a size in EMUs; grouped pictures sit inside a group shape.</summary>
    public sealed record Picture(string ImagePath, long Width, long Height, bool Grouped = false);

    public static string Write(string path, params Picture[] pictures)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new P.Presentation(
            new SlideMasterIdList(),
            new SlideIdList(),
            new SlideSize { Cx = 12_192_000, Cy = 6_858_000 },
            new NotesSize { Cx = 6_858_000, Cy = 9_144_000 },
            new DefaultTextStyle());

        var masterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>("rId1");
        layoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(EmptyTree()),
            new ColorMapOverride(new D.MasterColorMapping())) { Type = SlideLayoutValues.Blank };
        layoutPart.AddPart(masterPart, "rId1");
        masterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(EmptyTree()),
            new P.ColorMap
            {
                Background1 = D.ColorSchemeIndexValues.Light1, Text1 = D.ColorSchemeIndexValues.Dark1,
                Background2 = D.ColorSchemeIndexValues.Light2, Text2 = D.ColorSchemeIndexValues.Dark2,
                Accent1 = D.ColorSchemeIndexValues.Accent1, Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3, Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5, Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new SlideLayoutIdList(new SlideLayoutId { Id = 2147483649U, RelationshipId = "rId1" }),
            new TextStyles(new TitleStyle(), new BodyStyle(), new OtherStyle()));
        var themePart = masterPart.AddNewPart<ThemePart>("rId2");
        themePart.Theme = Theme();
        presentationPart.AddPart(themePart, "rId2");
        presentationPart.Presentation.SlideMasterIdList!.Append(new SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" });

        uint slideId = 256;
        var relId = 3;
        foreach (var picture in pictures)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>($"rId{relId}");
            slidePart.AddPart(layoutPart, "rId1");
            var imagePart = slidePart.AddImagePart(
                Path.GetExtension(picture.ImagePath).ToLowerInvariant() is ".png" ? ImagePartType.Png : ImagePartType.Jpeg, "rId2");
            using (var stream = File.OpenRead(picture.ImagePath)) imagePart.FeedData(stream);

            var pic = Pic(2, "rId2", picture.Width, picture.Height);
            var tree = EmptyTree();
            tree.Append(picture.Grouped
                ? new GroupShape(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 3, Name = "Group" }, new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new D.TransformGroup(
                        new D.Offset { X = 0, Y = 0 }, new D.Extents { Cx = picture.Width, Cy = picture.Height },
                        new D.ChildOffset { X = 0, Y = 0 }, new D.ChildExtents { Cx = picture.Width, Cy = picture.Height })),
                    pic)
                : pic);
            slidePart.Slide = new Slide(new CommonSlideData(tree), new ColorMapOverride(new D.MasterColorMapping()));
            presentationPart.Presentation.SlideIdList!.Append(new SlideId { Id = slideId++, RelationshipId = $"rId{relId++}" });
        }

        presentationPart.Presentation.Save();
        return path;
    }

    /// <summary>The errors the Open XML SDK's validator finds in a file (empty for a valid one).</summary>
    public static List<string> Validate(string path)
    {
        using var document = PresentationDocument.Open(path, false);
        return new DocumentFormat.OpenXml.Validation.OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document).Select(e => $"{e.Part?.Uri}: {e.Description}").ToList();
    }

    private static ShapeTree EmptyTree() => new(
        new NonVisualGroupShapeProperties(
            new NonVisualDrawingProperties { Id = 1, Name = "" }, new NonVisualGroupShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties()),
        new GroupShapeProperties(new D.TransformGroup()));

    private static P.Picture Pic(uint id, string relationship, long width, long height) => new(
        new NonVisualPictureProperties(
            new NonVisualDrawingProperties { Id = id, Name = $"Picture {id}" },
            new NonVisualPictureDrawingProperties(new D.PictureLocks { NoChangeAspect = true }),
            new ApplicationNonVisualDrawingProperties()),
        new BlipFill(new D.Blip { Embed = relationship }, new D.Stretch(new D.FillRectangle())),
        new ShapeProperties(
            new D.Transform2D(new D.Offset { X = Inch / 2, Y = Inch / 2 }, new D.Extents { Cx = width, Cy = height }),
            new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));

    private static D.Theme Theme()
    {
        static D.SolidFill Solid(string hex) => new(new D.RgbColorModelHex { Val = hex });
        static D.SolidFill Phc() => new(new D.SchemeColor { Val = D.SchemeColorValues.PhColor });
        D.Outline Line() => new(Phc()) { Width = 9525 };
        D.EffectStyle Effect() => new(new D.EffectList());
        return new D.Theme(
            new D.ThemeElements(
                new D.ColorScheme(
                    new D.Dark1Color(new D.SystemColor { Val = D.SystemColorValues.WindowText, LastColor = "000000" }),
                    new D.Light1Color(new D.SystemColor { Val = D.SystemColorValues.Window, LastColor = "FFFFFF" }),
                    new D.Dark2Color(new D.RgbColorModelHex { Val = "1F497D" }),
                    new D.Light2Color(new D.RgbColorModelHex { Val = "EEECE1" }),
                    new D.Accent1Color(new D.RgbColorModelHex { Val = "4F81BD" }),
                    new D.Accent2Color(new D.RgbColorModelHex { Val = "C0504D" }),
                    new D.Accent3Color(new D.RgbColorModelHex { Val = "9BBB59" }),
                    new D.Accent4Color(new D.RgbColorModelHex { Val = "8064A2" }),
                    new D.Accent5Color(new D.RgbColorModelHex { Val = "4BACC6" }),
                    new D.Accent6Color(new D.RgbColorModelHex { Val = "F79646" }),
                    new D.Hyperlink(new D.RgbColorModelHex { Val = "0000FF" }),
                    new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = "800080" })) { Name = "Office" },
                new D.FontScheme(
                    new D.MajorFont(new D.LatinFont { Typeface = "Calibri" }, new D.EastAsianFont { Typeface = "" }, new D.ComplexScriptFont { Typeface = "" }),
                    new D.MinorFont(new D.LatinFont { Typeface = "Calibri" }, new D.EastAsianFont { Typeface = "" }, new D.ComplexScriptFont { Typeface = "" }))
                { Name = "Office" },
                new D.FormatScheme(
                    new D.FillStyleList(Phc(), Solid("FFFFFF"), Solid("000000")),
                    new D.LineStyleList(Line(), Line(), Line()),
                    new D.EffectStyleList(Effect(), Effect(), Effect()),
                    new D.BackgroundFillStyleList(Phc(), Solid("FFFFFF"), Solid("000000")))
                { Name = "Office" }),
            new D.ObjectDefaults(),
            new D.ExtraColorSchemeList()) { Name = "Office Theme" };
    }
}
