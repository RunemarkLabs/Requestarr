import React from 'react';
import useTheme from 'Helpers/Hooks/useTheme';

interface HeaderLogoProps {
  className: string;
}

// The logo of the environment the app runs in (production, dev, qa or beta), in the theme the app shows; Auto
// follows the system setting while the page is open. Dev, QA and beta have their branch bar beside the R; production's
// bar shows the running version, which the server draws.
function HeaderLogo({ className }: HeaderLogoProps) {
  const theme = useTheme() === 'dark' ? 'dark' : 'light';
  const { urlBase } = window.Requestarr;
  const iconSet = window.Requestarr.iconSet || 'production';
  const file =
    iconSet === 'production' ? 'logo-version.svg' : 'logo-header.png';

  return (
    <img
      className={className}
      src={`${urlBase}/Content/Images/Icons/${theme}/${iconSet}/${file}`}
      alt="Requestarr Logo"
    />
  );
}

export default HeaderLogo;
