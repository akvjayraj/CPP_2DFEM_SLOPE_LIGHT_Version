$env:PATH -split ';' | Where-Object { $_ -match 'python|conda|pandoc|node' } | Write-Output
Get-ChildItem 'C:\Python*' -ErrorAction SilentlyContinue | Select-Object FullName
Get-ChildItem 'C:\Program Files\Pandoc' -ErrorAction SilentlyContinue | Select-Object FullName
Get-Command python -ErrorAction SilentlyContinue | Select-Object Source
Get-Command pandoc -ErrorAction SilentlyContinue | Select-Object Source
Get-Command node -ErrorAction SilentlyContinue | Select-Object Source
Get-Command npm -ErrorAction SilentlyContinue | Select-Object Source
