import React from 'react';
import useTheme from 'Helpers/Hooks/useTheme';

interface HeaderLogoProps {
  className: string;
}

// The logo of the environment the app runs in (production, dev, qa or beta), in the theme the app shows; Auto
// follows the system setting while the page is open. The light theme's header is purple, so it has its own set
// (light-purple: beta's bar is white there). Dev, QA and beta have their branch bar beside the R; production's bar
// shows the running version, which the server draws. Pointing at the logo shows the version everywhere (beta's is its
// beta version, 0.4.17-beta.1).
function HeaderLogo({ className }: HeaderLogoProps) {
  const theme = useTheme() === 'dark' ? 'dark' : 'light-purple';
  const { urlBase, releaseVersion, version } = window.Requestarr;
  const iconSet = window.Requestarr.iconSet || 'production';
  const file =
    iconSet === 'production' ? 'logo-version.svg' : 'logo-header.png';

  return (
    <img
      className={className}
      src={`${urlBase}/Content/Images/Icons/${theme}/${iconSet}/${file}`}
      alt="Requestarr Logo"
      title={`Requestarr ${releaseVersion || version}`}
    />
  );
}

export default HeaderLogo;
