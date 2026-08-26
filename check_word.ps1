try {
    $w = New-Object -ComObject Word.Application
    Write-Output "Word available"
    $w.Quit()
} catch {
    Write-Output "Word not available"
}
