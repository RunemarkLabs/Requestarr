import React from 'react';
import { Redirect, Route } from 'react-router-dom';
import NotFound from 'Components/NotFound';
import Switch from 'Components/Router/Switch';
import DevelopmentSettingsConnector from 'Settings/Development/DevelopmentSettingsConnector';
import GeneralSettingsConnector from 'Settings/General/GeneralSettingsConnector';
import Settings from 'Settings/Settings';
import TagSettings from 'Settings/Tags/TagSettings';
import UISettingsConnector from 'Settings/UI/UISettingsConnector';
import BackupsConnector from 'System/Backup/BackupsConnector';
import LogsTableConnector from 'System/Events/LogsTableConnector';
import Logs from 'System/Logs/Logs';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import Updates from 'System/Updates/Updates';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';

function RedirectToSystemStatus() {
  return <Redirect to={getPathWithUrlBase('/system/status')} />;
}

function AppRoutes() {
  return (
    <Switch>
      {/*
        Home
      */}

      <Route exact={true} path="/" render={RedirectToSystemStatus} />

      {window.Requestarr.urlBase && (
        <Route
          exact={true}
          path="/"
          // eslint-disable-next-line @typescript-eslint/ban-ts-comment
          // @ts-ignore
          addUrlBase={false}
          render={RedirectToSystemStatus}
        />
      )}

      {/*
        Settings
      */}

      <Route exact={true} path="/settings" component={Settings} />

      <Route path="/settings/tags" component={TagSettings} />

      <Route path="/settings/general" component={GeneralSettingsConnector} />

      <Route path="/settings/ui" component={UISettingsConnector} />

      <Route
        path="/settings/development"
        component={DevelopmentSettingsConnector}
      />

      {/*
        System
      */}

      <Route path="/system/status" component={Status} />

      <Route path="/system/tasks" component={Tasks} />

      <Route path="/system/backup" component={BackupsConnector} />

      <Route path="/system/updates" component={Updates} />

      <Route path="/system/events" component={LogsTableConnector} />

      <Route path="/system/logs/files" component={Logs} />

      {/*
        Not Found
      */}

      <Route path="*" component={NotFound} />
    </Switch>
  );
}

export default AppRoutes;
