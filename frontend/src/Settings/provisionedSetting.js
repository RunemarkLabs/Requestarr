import translate from 'Utilities/String/translate';

// Settings the provisioning file sets on every start (the settings API lists them): a change made on the page would be
// undone on the next start, so they're shown read-only, saying where to change them.
export function isProvisioned(settings, name) {
  return settings.provisioned?.value?.includes(name) ?? false;
}

function helpTextFor(provisioned, helpText) {
  return provisioned ? translate('SetByProvisioningHelpText') : helpText;
}

export function provisionedHelpText(settings, name, helpText) {
  return helpTextFor(isProvisioned(settings, name), helpText);
}

// For text, number and password inputs.
export function readOnlyIfProvisioned(settings, name, helpText) {
  const provisioned = isProvisioned(settings, name);

  return { readOnly: provisioned, helpText: helpTextFor(provisioned, helpText) };
}

// For selects and checkboxes.
export function disabledIfProvisioned(settings, name, helpText) {
  const provisioned = isProvisioned(settings, name);

  return { isDisabled: provisioned, helpText: helpTextFor(provisioned, helpText) };
}
