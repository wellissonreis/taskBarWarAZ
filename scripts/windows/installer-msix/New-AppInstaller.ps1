[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$BaseUri = 'https://taskbarwaraz.blob.core.windows.net/downloads/'
)
# Gera o SideWar.appinstaller a partir do manifesto que esta DENTRO do msix e valida
# com o leitor do proprio Windows (AppxPackaging.dll, schema do App Installer embutido).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$msix = Get-Item -LiteralPath $MsixPath
$zip = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
$id = $manifest.Package.Identity

$ns = 'http://schemas.microsoft.com/appx/appinstaller/2018'
$selfUri = $BaseUri + 'SideWar.appinstaller'
$out = [IO.Path]::GetFullPath($OutputPath)
$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$w = [Xml.XmlWriter]::Create($out, $settings)
try {
    $w.WriteStartDocument()
    $w.WriteStartElement('AppInstaller', $ns)
    $w.WriteAttributeString('Uri', $selfUri)
    $w.WriteAttributeString('Version', $id.Version)
    $w.WriteStartElement('MainPackage', $ns)
    $w.WriteAttributeString('Name', $id.Name)
    $w.WriteAttributeString('Publisher', $id.Publisher)
    $w.WriteAttributeString('Version', $id.Version)
    $w.WriteAttributeString('ProcessorArchitecture', $id.ProcessorArchitecture)
    $w.WriteAttributeString('Uri', $BaseUri + $msix.Name)
    $w.WriteEndElement()
    $w.WriteStartElement('UpdateSettings', $ns)
    $w.WriteStartElement('OnLaunch', $ns)
    $w.WriteAttributeString('HoursBetweenUpdateChecks', '0')
    $w.WriteAttributeString('ShowPrompt', 'false')
    $w.WriteAttributeString('UpdateBlocksActivation', 'false')
    $w.WriteEndElement()
    $w.WriteStartElement('AutomaticBackgroundTask', $ns)
    $w.WriteEndElement()
    $w.WriteElementString('ForceUpdateFromAnyVersion', $ns, 'true')
    $w.WriteEndElement()
    $w.WriteEndElement()
    $w.WriteEndDocument()
} finally { $w.Dispose() }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class SideWarAppInstallerCheck
{
    // Os dois primeiros metodos so ocupam as posicoes da vtable de IAppxFactory3.
    [ComImport, Guid("776b2c05-e21d-4e24-ba1a-cd529a8bfdbb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAppxFactory3
    {
        void CreatePackageReader2();
        void CreateManifestReader2();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object CreateAppInstallerReader(IStream inputStream, [MarshalAs(UnmanagedType.LPWStr)] string expectedDigest);
    }

    [ComImport, Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781")]
    private class AppxFactory { }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern IStream SHCreateStreamOnFileW(string file, uint mode);

    // Lanca COMException se o arquivo nao obedecer ao schema.
    public static void Validate(string path)
    {
        var factory = (IAppxFactory3)new AppxFactory();
        var reader = factory.CreateAppInstallerReader(SHCreateStreamOnFileW(path, 0x20), null);
        Marshal.ReleaseComObject(reader);
    }
}
'@
try { [SideWarAppInstallerCheck]::Validate($out) }
catch { throw "SideWar.appinstaller rejeitado pelo schema do App Installer: $($_.Exception.Message)" }
Write-Host "App Installer: $out (versao $($id.Version), validado pelo schema do Windows)"
