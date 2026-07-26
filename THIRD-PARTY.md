# Third-party dependency

## VirtualDesktop (PowerShell module)

This kit requires the community PowerShell module **VirtualDesktop** by Markus Scholtes:

- Gallery: https://www.powershellgallery.com/packages/VirtualDesktop
- Source: https://github.com/MScholtes/PSVirtualDesktop

It wraps undocumented Windows virtual-desktop COM interfaces. Microsoft does not
provide a full public API for moving arbitrary windows between desktops.

Install via `setup.ps1` or:

```powershell
Install-Module VirtualDesktop -Scope CurrentUser
```

License terms are those of the VirtualDesktop package on the PowerShell Gallery /
GitHub repository above. This kit does not redistribute that module; it installs
it from the Gallery at setup time.
