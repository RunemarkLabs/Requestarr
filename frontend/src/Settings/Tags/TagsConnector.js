import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchTagDetails, fetchTags } from 'Store/Actions/tagActions';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import sortByProp from 'Utilities/Array/sortByProp';
import Tags from './Tags';

function createMapStateToProps() {
  return createSelector(
    createSortedSectionSelector('tags', sortByProp('label')),
    (tags) => {
      const isFetching = tags.isFetching || tags.details.isFetching;
      const error = tags.error || tags.details.error;
      const isPopulated = tags.isPopulated && tags.details.isPopulated;

      return {
        ...tags,
        isFetching,
        error,
        isPopulated
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchTags: fetchTags,
  dispatchFetchTagDetails: fetchTagDetails
};

class TagsConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      dispatchFetchTags,
      dispatchFetchTagDetails
    } = this.props;

    dispatchFetchTags();
    dispatchFetchTagDetails();
  }

  //
  // Render

  render() {
    return (
      <Tags
        {...this.props}
      />
    );
  }
}

TagsConnector.propTypes = {
  dispatchFetchTags: PropTypes.func.isRequired,
  dispatchFetchTagDetails: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(TagsConnector);
