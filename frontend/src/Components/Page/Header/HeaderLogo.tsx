import React from 'react';
import useTheme from 'Helpers/Hooks/useTheme';

interface HeaderLogoProps {
  className: string;
}

// The logo of the environment the app runs in (production, dev, qa or beta), in the theme the app shows; Auto
// follows the system setting while the page is open.
function HeaderLogo({ className }: HeaderLogoProps) {
  const theme = useTheme() === 'dark' ? 'dark' : 'light';
  const { urlBase, iconSet } = window.Requestarr;

  return (
    <img
      className={className}
      src={`${urlBase}/Content/Images/Icons/${theme}/${
        iconSet || 'production'
      }/logo.png`}
      alt="Requestarr Logo"
    />
  );
}

export default HeaderLogo;
