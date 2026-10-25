import { useCallback, useEffect } from 'react';
import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import themes from 'Styles/Themes';
import AppState from './State/AppState';

function createThemeSelector() {
  return createSelector(
    (state: AppState) =>
      state.settings.ui.item.theme || window.Requestarr.theme,
    (theme) => {
      return theme;
    }
  );
}

function ApplyTheme() {
  const theme = useSelector(createThemeSelector());

  const updateCSSVariables = useCallback(() => {
    Object.entries(themes[theme]).forEach(([key, value]) => {
      document.documentElement.style.setProperty(`--${key}`, value);
    });
  }, [theme]);

  // On Component Mount and Component Update
  useEffect(() => {
    updateCSSVariables();

    // The icons follow the theme too (index.ejs): light or dark, Auto with the system setting.
    window.Requestarr.applyIconTheme?.(theme);
  }, [updateCSSVariables, theme]);

  return null;
}

export default ApplyTheme;
