import React from 'react';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import LoadingMessage from 'Components/Loading/LoadingMessage';
import HeaderLogo from 'Components/Page/Header/HeaderLogo';
import styles from './LoadingPage.css';

// While the app loads: the same logo the header shows for the environment and theme.
function LoadingPage() {
  return (
    <div className={styles.page}>
      <HeaderLogo className={styles.logo} />
      <LoadingMessage />
      <LoadingIndicator />
    </div>
  );
}

export default LoadingPage;
