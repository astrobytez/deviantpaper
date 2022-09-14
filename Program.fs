open System
open System.IO
open FSharp.Data
open System.Diagnostics
open System.Threading.Tasks

/// TODO
/// [x] Take seed from CLI or other source - from time
/// [0] Make bolero app build as standalone app - reject
/// [] Wallhaven API supports query selection of images size
/// [] Create images directory if missing.
/// [] DeviantArt API sucks - build fixes or remove module

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
        |> Async.Catch
        |> Async.RunSynchronously

    let download url fileName =
        match getUrlAsync url fileName with
        | Choice1Of2 a -> Ok $"Success download {url}"
        | _ -> Error $"Failed to download {url}"

    let projectRoot paths =
        [__SOURCE_DIRECTORY__] @ paths
        |> Array.ofList
        |> Path.Join

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

        let pipline execute =
            let results : Command.CommandResult =
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

        /// Find path to D-Bus (usually /usr/bin/qdbus)
        let result =
            Command.executeCommand "type" (["-p"; "qdbus"] |> Seq.ofList)
            |> Async.RunSynchronously
        match result.ExitCode with
        | 0 -> (sprintf $"{result.StandardOutput}").Trim() |> Command.executeCommand |> pipline
        | _ -> ()

open Utilities

module DeviantArt =
    let chooseRandom = chooseRandom' Seed
    type DeviantArt = XmlProvider<"sample.xml", ResolutionFolder=__SOURCE_DIRECTORY__>

    let getImages (query: string) =
        printfn $"Query param: {query}"

        DeviantArt.Load(query).Channel.Items
        |> List.ofArray

    let setRandomFromArtist outDir artist =
        if not (Directory.Exists (projectRoot [outDir])) then
            try
                Directory.CreateDirectory (projectRoot [outDir]) |> ignore
            with exn ->
                printfn $"Failed to create output directory {outDir} - {exn}"

        let root = "https://backend.deviantart.com/rss.xml?type=deviation&q="
        let images = getImages (root + $"{artist}" + "+sort%3Atime+meta%3Aall")
        let select = chooseRandom images
        match download select.Content.Url (projectRoot [outDir; $"{select.Title}.png"]) with
        | Ok _ -> Wallpaper.setWallpaper (projectRoot [outDir; $"{select.Title}.png"])
        | Error msg -> printfn $"{msg}"

module WallHaven =
    type WallHaven = JsonProvider<"sample.json", ResolutionFolder=__SOURCE_DIRECTORY__>
    let root query = $"""https://wallhaven.cc/api/v1/search?q={query}&atleast=1920x1080&sorting=random"""

    let setRandomFromQuery outDir query =
        if not (Directory.Exists (projectRoot [outDir])) then
            try
                Directory.CreateDirectory (projectRoot [outDir]) |> ignore
            with exn ->
                printfn $"Failed to create output directory {outDir} - {exn}"

        let result = WallHaven.Load(root query)
        let images = result.Data |> List.ofArray

        let select =
            images |> List.head // query sets result sorted as random

        let fileName = Path.GetFileName select.Path
        match download select.Path (projectRoot [outDir; $"{fileName}.png"]) with
        | Ok _ -> Wallpaper.setWallpaper (projectRoot [outDir; $"{fileName}.png"])
        | Error msg -> printfn $"{msg}"

[<EntryPoint>]
let main args =

    // "digital+art"
    // "Anime+space+landscape"

    let query = 
        match args.Length with
        | n when n > 0 ->
            args
            |> Array.reduce (
                fun a b -> $"{a}+{b}"
            )
        | _ -> "Anime+space+landscape"

    printfn $"Query is '{query}'"
    WallHaven.setRandomFromQuery "images" query |> ignore
    0