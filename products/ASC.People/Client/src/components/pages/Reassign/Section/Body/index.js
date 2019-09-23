import React from "react";
import { withRouter } from "react-router";
// import { useTranslation } from 'react-i18next';
import { connect } from "react-redux";
import styled from 'styled-components';


const InfoContainer = styled.div`
  margin-bottom: 24px;
`;

const SectionBodyContent = props => {
  // const { t } = useTranslation();

  return (
    <InfoContainer>
      See this feature in next version!
  </InfoContainer>
  );
};

function mapStateToProps(state) {
  return {
  };
}

export default connect(mapStateToProps)(withRouter(SectionBodyContent));
