import PropTypes from 'prop-types';
import React from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes, sizes } from 'Helpers/Props';
import { disabledIfProvisioned } from 'Settings/provisionedSetting';
import translate from 'Utilities/String/translate';

function AnalyticSettings(props) {
  const {
    settings,
    onInputChange
  } = props;

  const {
    analyticsEnabled
  } = settings;

  return (
    <FieldSet legend={translate('Analytics')}>
      <FormGroup size={sizes.MEDIUM}>
        <FormLabel>{translate('SendAnonymousUsageData')}</FormLabel>

        <FormInputGroup
          type={inputTypes.CHECK}
          name="analyticsEnabled"
          {...disabledIfProvisioned(settings, 'analyticsEnabled', translate('AnalyticsEnabledHelpText'))}
          helpTextWarning={translate('RestartRequiredHelpTextWarning')}
          onChange={onInputChange}
          {...analyticsEnabled}
        />
      </FormGroup>
    </FieldSet>
  );
}

AnalyticSettings.propTypes = {
  settings: PropTypes.object.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default AnalyticSettings;
