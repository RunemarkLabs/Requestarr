declare module '*.module.css';

interface Window {
  Requestarr: {
    apiKey: string;
    instanceName: string;
    theme: string;
    urlBase: string;
    iconSet: string;
    applyIconTheme?: (theme: string) => string;
    version: string;
  };
}
