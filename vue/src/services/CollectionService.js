import axios from 'axios';

export default {
    getUserCollections() {
        return axios.get("/user/collection");
    },

    addCollection(collection) {
        return axios.post("/user/collection", collection);
    },

    getComicsInCollection(collectionID) {
        return axios.get(`/user/collection/${collectionID}`);
    },

    addComicToCollection(collection, comic) {
        return axios.post(`/user/collection/${collection.collectionID}`, comic);
    },

    updateCollectionSettings(collection) {
        return axios.put(`/user/collection/${collection.collectionID}`, collection);
    },

    getPublicCollections() {
        return axios.get("/anonymous/collection");
    }
}