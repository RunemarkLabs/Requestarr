import React from 'react';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface HealthItemLinkProps {
  source: string;
}

function HealthItemLink(props: HealthItemLinkProps) {
  const { source } = props;

  switch (source) {
    case 'UpdateCheck':
      return (
        <IconButton
          name={icons.UPDATE}
          title={translate('Updates')}
          to="/system/updates"
        />
      );
    default:
      return null;
  }
}

export default HealthItemLink;
