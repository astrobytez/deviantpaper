open System
open System.Diagnostics
open System.Threading.Tasks
open FSharp.Data

/// TODO:
/// [] Test image download functions
/// [] Run program at startup
/// [] Build a config parser - json?
/// [] Build a UI frontend?
/// [] Setup ability to setup different wallpapers on each display - javascript
/// [] Get the screen resolution programmatically

module Utilities =
    let chooseRandom seed =
        let rng = Random(seed)
        let wrapped (list : list<'a>) =
            list.[rng.Next(0, list.Length - 1)]
        wrapped

    let shuffle seed =
        let rng = Random(seed)
        let wrapped (list : list<'a>) =
            [for i in 0..(list.Length-1) -> list.[rng.Next(0, list.Length - 1)]]
        wrapped

    let getUrlAsync url fileName =
        async {
            let! request = Http.AsyncRequestStream(url)
            use outputFile = new System.IO.FileStream(fileName, System.IO.FileMode.Create)
            do! request.ResponseStream.CopyToAsync( outputFile ) |> Async.AwaitTask
        } |> Async.RunSynchronously



module DeviantArt =
    type DeviantArt = XmlProvider<"rss.xml", ResolutionFolder=__SOURCE_DIRECTORY__>

    let chooseRandom = Utilities.chooseRandom 0

    /// Get all nature images in RSS feed.
    /// Select a random item from the list.
    /// Download & save the Image payload if the screen size is 'large'

    let query = "https://backend.deviantart.com/rss.xml?type=tags&tag=nature&limit=5"
    let data = DeviantArt.Load(query)
    let image =
        let items = data.Channel.Items
        seq { for each in items do
                if each.Content.Width > 1280 && each.Content.Height > 1024 then each }
        |> List.ofSeq
        |> chooseRandom

    let imageData =
        let fileName =
            let url = image.Content.Url
            url // TODO get file extension from URL and build filename

        Utilities.getUrlAsync image.Content.Url $"{image.Title}.png"

module Command =

    // Adopted from https://alexn.org/blog/2020/12/06/execute-shell-command-in-fsharp/
    type CommandResult = {
        ExitCode: int
        StandardOutput: string
        StandardError: string
    }

    let executeCommand executable args =
        async {
            let startInfo = ProcessStartInfo()
            startInfo.FileName <- executable
            for a in args do
                startInfo.ArgumentList.Add(a)
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.UseShellExecute <- false
            startInfo.CreateNoWindow <- true
            use p = new Process()
            p.StartInfo <- startInfo
            p.Start() |> ignore

            let outTask = Task.WhenAll([|
                p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync()
            |])

            do! p.WaitForExitAsync() |> Async.AwaitTask
            let! out = outTask |> Async.AwaitTask
            return {
                ExitCode = p.ExitCode;
                StandardOutput = out.[0];
                StandardError = out.[1]
            }
        }

module Wallpaper =

    let command data =
        let cmd =
            "org.kde.plasmashell /PlasmaShell org.kde.PlasmaShell.evaluateScript".Split()
            |> List.ofArray
        cmd @ [data]
        |> Seq.ofList

    let payload file = $"""
        var allDesktops = desktops();
        print (allDesktops);

        for (i=0;i<allDesktops.length;i++) {{
            d = allDesktops[i];
            d.wallpaperPlugin = "org.kde.image";
            d.currentConfigGroup = Array("Wallpaper", "org.kde.image", "General");
            d.writeConfig("Image", "{file}")}}"""

    let SetWallpaper path =
        let execute = Command.executeCommand "/usr/bin/qdbus"
        let results =
            path
            |> payload
            |> command
            |> execute |> Async.RunSynchronously
        if results.ExitCode = 0 then
            printfn $"{results.StandardOutput}"
        else
            eprintfn $"{results.StandardError}"
            Environment.Exit(results.ExitCode)

Wallpaper.SetWallpaper "/home/engineer/Downloads/superstructure_by_aeon_lux_d87imh9.jpg"