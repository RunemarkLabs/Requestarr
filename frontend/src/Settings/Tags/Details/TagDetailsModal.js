import PropTypes from 'prop-types';
import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import TagDetailsModalContent from './TagDetailsModalContent';

function TagDetailsModal(props) {
  const {
    isOpen,
    onModalClose,
    ...otherProps
  } = props;

  return (
    <Modal
      size={sizes.SMALL}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <TagDetailsModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

TagDetailsModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default TagDetailsModal;
