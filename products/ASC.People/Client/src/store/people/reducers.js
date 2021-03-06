import {
  SET_GROUPS,
  SET_USERS,
  SET_SELECTION,
  SELECT_USER,
  DESELECT_USER,
  SET_SELECTED,
  SET_FILTER,
  SELECT_GROUP,
  SET_USER,
  SET_SELECTOR_USERS
} from "./actions";
import { isUserSelected, skipUser, getUsersBySelected } from "./selectors";
import { api } from "asc-web-common";
const { Filter } = api;

const initialState = {
  users: null,
  groups: [],
  selection: [],
  selected: "none",
  selectedGroup: null,
  filter: Filter.getDefault(),
  selector: {
    users: []
  }
};

const peopleReducer = (state = initialState, action) => {
  switch (action.type) {
    case SET_GROUPS:
      return Object.assign({}, state, {
        groups: action.groups
      });
    case SET_USERS:
      return Object.assign({}, state, {
        users: action.users
      });
    case SET_USER:
      return Object.assign({}, state, {
        users: state.users.map(user =>
          user.id === action.user.id ? action.user : user
        )
      });
    case SET_SELECTION:
      return Object.assign({}, state, {
        selection: action.selection
      });
    case SELECT_USER:
      if (!isUserSelected(state.selection, action.user.id)) {
        return Object.assign({}, state, {
          selection: [...state.selection, action.user]
        });
      } else return state;
    case DESELECT_USER:
      if (isUserSelected(state.selection, action.user.id)) {
        return Object.assign({}, state, {
          selection: skipUser(state.selection, action.user.id)
        });
      } else return state;
    case SET_SELECTED:
      return Object.assign({}, state, {
        selected: action.selected,
        selection: getUsersBySelected(state.users, action.selected)
      });
    case SET_FILTER:
      return Object.assign({}, state, {
        filter: action.filter
      });
    case SELECT_GROUP:
      return Object.assign({}, state, {
        selectedGroup: action.groupId
      });
    case SET_SELECTOR_USERS:
      return Object.assign({}, state, {
        selector: Object.assign({}, state.selector, { 
          users: action.users
        })
      });
    default:
      return state;
  }
};

export default peopleReducer;
