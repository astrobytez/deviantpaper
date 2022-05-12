﻿open System
open System.IO
open FSharp.Data
open System.Diagnostics
open System.Threading.Tasks

// TODO
// [] Take seed from CLI or other source
// [] Make bolero app build as standalone app

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

module DeviantArt =
    type DeviantArt = XmlProvider<"sample.xml", ResolutionFolder=__SOURCE_DIRECTORY__>

    let chooseRandom = Utilities.chooseRandom' Utilities.Seed

    let getImages (query: string) =
        printfn $"Query param: {query}"

        DeviantArt.Load(query).Channel.Items
        |> List.ofArray

    let download url fileName = Utilities.getUrlAsync url fileName

    let setRandomFromArtist artist =
        let root = "https://backend.deviantart.com/rss.xml?type=deviation&q="
        let images = getImages (root + $"{artist}" + "+sort%3Atime+meta%3Aall")
        let select = chooseRandom images
        download select.Content.Url $"images/{select.Title}.png"

        Wallpaper.setWallpaper (
            Path.Join [| __SOURCE_DIRECTORY__
                         $"images/{select.Title}.png" |]
        )

let deviant = "mr-singh-art"
DeviantArt.setRandomFromArtist deviant