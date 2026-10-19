import PropTypes from 'prop-types';
import React from 'react';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import styles from './ErrorPage.css';

function ErrorPage(props) {
  const {
    version,
    isLocalStorageSupported,
    translationsError,
    customFiltersError,
    tagsError,
    uiSettingsError,
    generalSettingsError,
    systemStatusError
  } = props;

  let errorMessage = 'Failed to load Requestarr';

  if (!isLocalStorageSupported) {
    errorMessage = 'Local Storage is not supported or disabled. A plugin or private browsing may have disabled it.';
  } else if (translationsError) {
    errorMessage = getErrorMessage(translationsError, 'Failed to load translations from API');
  } else if (customFiltersError) {
    errorMessage = getErrorMessage(customFiltersError, 'Failed to load custom filters from API');
  } else if (tagsError) {
    errorMessage = getErrorMessage(tagsError, 'Failed to load tags from API');
  } else if (uiSettingsError) {
    errorMessage = getErrorMessage(uiSettingsError, 'Failed to load UI settings from API');
  } else if (generalSettingsError) {
    errorMessage = getErrorMessage(generalSettingsError, 'Failed to load general settings from API');
  } else if (systemStatusError) {
    errorMessage = getErrorMessage(systemStatusError, 'Failed to load system status from API');
  }

  return (
    <div className={styles.page}>
      <div className={styles.errorMessage}>
        {errorMessage}
      </div>

      <div className={styles.version}>
        Version {version}
      </div>
    </div>
  );
}

ErrorPage.propTypes = {
  version: PropTypes.string.isRequired,
  isLocalStorageSupported: PropTypes.bool.isRequired,
  translationsError: PropTypes.object,
  customFiltersError: PropTypes.object,
  tagsError: PropTypes.object,
  uiSettingsError: PropTypes.object,
  generalSettingsError: PropTypes.object,
  systemStatusError: PropTypes.object
};

export default ErrorPage;
