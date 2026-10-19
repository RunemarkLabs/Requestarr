import {
  AppSectionItemState,
  AppSectionSaveState,
} from 'App/State/AppSectionState';
import General from 'typings/Settings/General';
import UiSettings from 'typings/Settings/UiSettings';

export interface GeneralAppState
  extends AppSectionItemState<General>,
    AppSectionSaveState {}

export type UiSettingsAppState = AppSectionItemState<UiSettings>;

interface SettingsAppState {
  general: GeneralAppState;
  ui: UiSettingsAppState;
}

export default SettingsAppState;
