import React from 'react';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import LoadingMessage from 'Components/Loading/LoadingMessage';
import styles from './LoadingPage.css';

const requestarrLogo = 'data:image/svg+xml;base64,PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0iVVRGLTgiPz4KPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI1MTIiIGhlaWdodD0iNTEyIiB2aWV3Qm94PSIwIDAgNTEyIDUxMiI+CiAgPHRpdGxlPlJlcXVlc3RhcnI8L3RpdGxlPgogIDxyZWN0IHg9IjAiIHk9IjAiIHdpZHRoPSI1MTIiIGhlaWdodD0iNTEyIiByeD0iMTEyIiByeT0iMTEyIiBmaWxsPSIjNzA0OGU4Ii8+CiAgPHBhdGggZD0iTTE3NiAzOTIgVjEyOCBIMjc2IEE4MCA4MCAwIDAgMSAyNzYgMjg4IEgxNzYgTTI2OCAyODggTDM0NCAzOTIiIGZpbGw9Im5vbmUiIHN0cm9rZT0iI2ZmZmZmZiIgc3Ryb2tlLXdpZHRoPSI1NiIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIi8+Cjwvc3ZnPgo=';

function LoadingPage() {
  return (
    <div className={styles.page}>
      <img
        className={styles.logoFull}
        src={requestarrLogo}
      />
      <LoadingMessage />
      <LoadingIndicator />
    </div>
  );
}

export default LoadingPage;
