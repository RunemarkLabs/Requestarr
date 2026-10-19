import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Card from 'Components/Card';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import TagDetailsModal from './Details/TagDetailsModal';
import styles from './Tag.css';

class Tag extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false,
      isDeleteTagModalOpen: false
    };
  }

  //
  // Listeners

  onShowDetailsPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  onDeleteTagPress = () => {
    this.setState({
      isDetailsModalOpen: false,
      isDeleteTagModalOpen: true
    });
  };

  onDeleteTagModalClose= () => {
    this.setState({ isDeleteTagModalOpen: false });
  };

  onConfirmDeleteTag = () => {
    this.props.onConfirmDeleteTag({ id: this.props.id });
  };

  //
  // Render

  render() {
    const {
      label
    } = this.props;

    const {
      isDetailsModalOpen,
      isDeleteTagModalOpen
    } = this.state;

    // Nothing in Requestarr references tags yet, so a tag is never in use.
    const isTagUsed = false;

    return (
      <Card
        className={styles.tag}
        overlayContent={true}
        onPress={this.onShowDetailsPress}
      >
        <div className={styles.label}>
          {label}
        </div>

        {
          !isTagUsed &&
            <div>
              {translate('NoLinks')}
            </div>
        }

        <TagDetailsModal
          label={label}
          isTagUsed={isTagUsed}
          isOpen={isDetailsModalOpen}
          onModalClose={this.onDetailsModalClose}
          onDeleteTagPress={this.onDeleteTagPress}
        />

        <ConfirmModal
          isOpen={isDeleteTagModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteTag')}
          message={translate('DeleteTagMessageText', { label })}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDeleteTag}
          onCancel={this.onDeleteTagModalClose}
        />
      </Card>
    );
  }
}

Tag.propTypes = {
  id: PropTypes.number.isRequired,
  label: PropTypes.string.isRequired,
  onConfirmDeleteTag: PropTypes.func.isRequired
};

export default Tag;
