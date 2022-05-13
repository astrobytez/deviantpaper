open System
open System.IO
open FSharp.Data
open System.Diagnostics
open System.Threading.Tasks

/// TODO
/// [x] Take seed from CLI or other source - from time
/// [0] Make bolero app build as standalone app - reject
/// [] Wallhaven API supports query selection of images size
/// 


module Utilities =
    let Seed = int (DateTimeOffset.Now.ToUnixTimeSeconds()) // Use time as seed to ensure different result each time

    let chooseRandom' seed =
        let rng = Random(seed)
        let wrapped (list: list<'a>) = list.[rng.Next(0, list.Length - 1)]
        wrapped

    let chooseRandom (list: list<'a>) = chooseRandom' Seed

    let getUrlAsync url fileName =
        async {
            let! request = Http.AsyncRequestStream(url)
            use outputFile = new System.IO.FileStream(fileName, System.IO.FileMode.Create)

            do!
                request.ResponseStream.CopyToAsync(outputFile)
                |> Async.AwaitTask
        }
        |> Async.RunSynchronously

    let download url fileName = getUrlAsync url fileName

    let projectRoot path =
        Path.Join [|__SOURCE_DIRECTORY__; path|]

module Command =

    // Adopted from https://alexn.org/blog/2020/12/06/execute-shell-command-in-fsharp/
    type CommandResult =
        { ExitCode: int
          StandardOutput: string
          StandardError: string }

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

            let outTask =
                Task.WhenAll(
                    [| p.StandardOutput.ReadToEndAsync()
                       p.StandardError.ReadToEndAsync() |]
                )

            do! p.WaitForExitAsync() |> Async.AwaitTask
            let! out = outTask |> Async.AwaitTask

            return
                { ExitCode = p.ExitCode
                  StandardOutput = out.[0]
                  StandardError = out.[1] }
        }

module Wallpaper =

    let command data =
        let cmd =
            "org.kde.plasmashell /PlasmaShell org.kde.PlasmaShell.evaluateScript"
                .Split()
            |> List.ofArray

        cmd @ [ data ] |> Seq.ofList

    let payload file =
        $"""
        var allDesktops = desktops();
        print (allDesktops);

        for (i=0;i<allDesktops.length;i++) {{
            d = allDesktops[i];
            d.wallpaperPlugin = "org.kde.image";
            d.currentConfigGroup = Array("Wallpaper", "org.kde.image", "General");
            d.writeConfig("Image", "{file}")}}"""

    let setWallpaper path =
        let execute = Command.executeCommand "/usr/bin/qdbus"

        let results =
            path
            |> payload
            |> command
            |> execute
            |> Async.RunSynchronously

        if results.ExitCode = 0 then
            printfn $"{results.StandardOutput}"
        else
            eprintfn $"{results.StandardError}"
            Environment.Exit(results.ExitCode)

open Utilities

module DeviantArt =
    let chooseRandom = chooseRandom' Seed
    type DeviantArt = XmlProvider<"sample.xml", ResolutionFolder=__SOURCE_DIRECTORY__>

    let getImages (query: string) =
        printfn $"Query param: {query}"

        DeviantArt.Load(query).Channel.Items
        |> List.ofArray

    let setRandomFromArtist artist =
        let root = "https://backend.deviantart.com/rss.xml?type=deviation&q="
        let images = getImages (root + $"{artist}" + "+sort%3Atime+meta%3Aall")
        let select = chooseRandom images
        download select.Content.Url (projectRoot $"images/{select.Title}.png")

        Wallpaper.setWallpaper (projectRoot $"images/{select.Title}.png")

module WallHaven =
    type WallHaven = JsonProvider<"sample.json", ResolutionFolder=__SOURCE_DIRECTORY__>
    let root = "https://wallhaven.cc/api/v1/search?q=nature&atleast=1920x1080&sorting=random"

    let setRandomFromQuery () =
        let result = WallHaven.Load(root)
        let images = result.Data |> List.ofArray

        let select =
            images |> List.head // query sets result sorted as random

        let fileName = Path.GetFileName select.Path
        download select.Path $"images/{fileName}"

        printfn $"Select: {fileName}"
        Wallpaper.setWallpaper (projectRoot $"images/{fileName}")

WallHaven.setRandomFromQuery () |> ignore
