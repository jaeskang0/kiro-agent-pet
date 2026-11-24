function Get-PetCatalog([string]$root) {
    [pscustomobject]@{
        Key = 'spark'; DisplayName = 'Spark'; Version = 1
        SpritePath = (Join-Path $PSScriptRoot 'assets\spark.png'); FrameColumns = $null
        HighestVisiblePixel = 64; MetadataPath = $null
    }
    $library = Join-Path $root 'pets'
    if (-not (Test-Path -LiteralPath $library)) { return }
    foreach ($folder in Get-ChildItem -LiteralPath $library -Directory) {
        if ($folder.Name.StartsWith('.')) { continue }
        try {
            $metadata = Get-Content -LiteralPath (Join-Path $folder.FullName 'pet.json') -Raw | ConvertFrom-Json
            $sprite = Join-Path $folder.FullName 'spritesheet.png'
            if ($metadata.key -ne $folder.Name -or $metadata.spriteVersionNumber -notin @(1, 2) -or
                -not $metadata.displayName -or -not (Test-Path -LiteralPath $sprite)) {
                throw 'Invalid installed pet metadata.'
            }
            [pscustomobject]@{
                Key = $metadata.key; DisplayName = $metadata.displayName
                Version = [int]$metadata.spriteVersionNumber; SpritePath = $sprite
                FrameColumns = $metadata.frameColumns
                HighestVisiblePixel = $metadata.highestVisiblePixel
                MetadataPath = (Join-Path $folder.FullName 'pet.json')
            }
        } catch {
            Write-Warning "Skipping invalid pet folder $($folder.Name): $($_.Exception.Message)"
        }
    }
}

function Get-HighestVisiblePixel($image) {
    if (-not ('PetAlphaTop' -as [type])) {
        Add-Type -TypeDefinition @'
public static class PetAlphaTop {
    public static int Find(byte[] pixels, int width, int height) {
        int highest = 208;
        for (int y = 0; y < height; y++) {
            int withinFrame = y % 208;
            if (withinFrame >= highest) continue;
            int end = (y * width + width) * 4;
            for (int index = y * width * 4 + 3; index < end; index += 4) {
                if (pixels[index] > 16) {
                    highest = withinFrame;
                    break;
                }
            }
            if (highest == 0) break;
        }
        return highest;
    }
}
'@
    }
    $converted = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new(
        $image, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0
    )
    $bytes = [byte[]]::new($image.PixelWidth * $image.PixelHeight * 4)
    $converted.CopyPixels($bytes, $image.PixelWidth * 4, 0)
    [PetAlphaTop]::Find($bytes, $image.PixelWidth, $image.PixelHeight)
}

function Read-PetGraphics($pet) {
    $rows = if ($pet.Version -eq 2) { 11 } else { 9 }
    $image = [System.Windows.Media.Imaging.BitmapImage]::new()
    $image.BeginInit()
    $image.UriSource = [uri](Resolve-Path -LiteralPath $pet.SpritePath).Path
    $image.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $image.EndInit()
    $image.Freeze()
    if ($image.PixelWidth -ne 1536 -or $image.PixelHeight -ne ($rows * 208)) {
        throw "Pet $($pet.DisplayName) has the wrong sheet size for sprite version $($pet.Version)."
    }
    $highestVisiblePixel = $pet.HighestVisiblePixel
    if ($null -eq $highestVisiblePixel) {
        $highestVisiblePixel = Get-HighestVisiblePixel $image
        if ($pet.MetadataPath) {
            $installed = Get-Content -LiteralPath $pet.MetadataPath -Raw | ConvertFrom-Json -AsHashtable
            $installed['highestVisiblePixel'] = $highestVisiblePixel
            [System.IO.File]::WriteAllText("$($pet.MetadataPath).tmp", ($installed | ConvertTo-Json -Depth 8))
            [System.IO.File]::Move("$($pet.MetadataPath).tmp", $pet.MetadataPath, $true)
        }
    }
    $newFrames = @{}
    for ($row = 0; $row -lt $rows; $row++) {
        $newFrames[$row] = @(
            for ($column = 0; $column -lt 8; $column++) {
                $frame = [System.Windows.Media.Imaging.CroppedBitmap]::new(
                    $image, [System.Windows.Int32Rect]::new($column * 192, $row * 208, 192, 208)
                )
                $frame.Freeze()
                $frame
            }
        )
    }
    $newClips = @{
        idle         = @{ Row = 0; Columns = @(0, 1, 2, 3, 4, 5, 4, 3, 2, 1); Loops = 8 }
        'walk-right' = @{ Row = 1; Columns = @(0, 1, 2, 3, 4, 5, 6, 7); Loops = 8 }
        'walk-left'  = @{ Row = 2; Columns = @(0, 1, 2, 3, 4, 5, 6, 7); Loops = 8 }
        wave         = @{ Row = 3; Columns = @(0, 1, 2, 3); Loops = 1 }
        jump         = @{ Row = 4; Columns = @(0, 1, 2, 3, 4, 3, 2, 1); Loops = 8 }
        surprise     = @{ Row = 5; Columns = @(0, 1, 2, 3, 2, 1); Loops = 12 }
        sleep        = @{ Row = 5; Columns = @(5, 4, 4, 5); Loops = 16 }
        worry        = @{ Row = 5; Columns = @(3, 6, 7, 6); Loops = 16 }
        eat          = @{ Row = 6; Columns = @(0, 1, 2, 3, 4, 5); Loops = 11 }
        excited      = @{ Row = 7; Columns = @(0, 1, 2, 3, 4, 5); Loops = 11 }
        read         = @{ Row = 8; Columns = @(0, 1, 2, 3, 4, 5); Loops = 13 }
    }
    $names = @('idle', 'walk-right', 'walk-left', 'wave', 'jump', 'surprise', 'sleep', 'worry', 'eat', 'excited', 'read')
    if ($rows -eq 11) {
        $newClips['turn-right'] = @{ Row = 9; Columns = @(0, 1, 2, 3, 4, 5, 6, 7); Loops = 8 }
        $newClips['turn-left'] = @{ Row = 10; Columns = @(0, 1, 2, 3, 4, 5, 6, 7); Loops = 8 }
        $names += @('turn-right', 'turn-left')
    }
    if ($pet.FrameColumns) {
        if ($pet.FrameColumns.Count -ne $rows -or -not $pet.FrameColumns[0].Count) {
            throw "Pet $($pet.DisplayName) has invalid visible-frame metadata."
        }
        foreach ($name in $names) {
            $clip = $newClips[$name]
            $available = @($pet.FrameColumns[$clip.Row])
            $clip.Columns = @($clip.Columns | Where-Object { $_ -in $available })
            if (-not $clip.Columns.Count) {
                if ($available.Count) {
                    $clip.Columns = $available
                } else {
                    $clip.Row = 0
                    $clip.Columns = @($pet.FrameColumns[0])
                }
            }
        }
        for ($row = 0; $row -lt $rows; $row++) {
            $rowNames = @($names | Where-Object { $newClips[$_].Row -eq $row })
            $used = @($rowNames | ForEach-Object { $newClips[$_].Columns })
            $unused = @($pet.FrameColumns[$row] | Where-Object { $_ -notin $used })
            if ($unused.Count -and $rowNames.Count) { $newClips[$rowNames[0]].Columns += $unused }
        }
        foreach ($name in $names) {
            $clip = $newClips[$name]
            if ($name -ne 'wave') {
                $desired = if ($name -in @('idle', 'read')) { 14000 } else { 12000 }
                $clip.Loops = [int][Math]::Round($desired / ($clip.Columns.Count * 180))
            }
        }
    }
    [pscustomobject]@{
        Frames = $newFrames; Clips = $newClips; Names = $names; Rows = $rows
        HighestVisiblePixel = [int]$highestVisiblePixel
    }
}
