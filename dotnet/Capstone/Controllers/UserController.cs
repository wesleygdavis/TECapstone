﻿using Capstone.DAO;
using Capstone.Models;
using Capstone.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Transactions;

namespace Capstone.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserDAO userDAO;
        private readonly ICollectionDAO collectionDAO;
        private readonly IComicDAO comicDAO;
        private readonly IComicVineService comicVine;
        private readonly ICharacterDAO characterDAO;
        private readonly ICreatorDAO creatorDAO;

        public UserController(ICollectionDAO collectionDAO, IComicDAO comicDAO, IComicVineService comicVine, IUserDAO userDAO, ICharacterDAO characterDAO, ICreatorDAO creatorDAO)
        {
            this.collectionDAO = collectionDAO;
            this.comicDAO = comicDAO;
            this.comicVine = comicVine;
            this.userDAO = userDAO;
            this.characterDAO = characterDAO;
            this.creatorDAO = creatorDAO;
        }
        private int GetUserIdFromToken()
        {
            string userIdStr = User.FindFirst("sub")?.Value;

            return Convert.ToInt32(userIdStr);
        }

        /// <summary>
        /// Gets a collection matching the parameter, 
        /// then compares the current user's id against the collection id.
        /// </summary>
        /// <param name="collectionId"></param>
        /// <returns>A boolean representing a match or not.</returns>
        private bool VerifyActiveUserOwnsCollection(int collectionId)
        {
            bool userOwns = false;
            Collection collection = collectionDAO.GetSingleCollection(collectionId);
            int userID = GetUserIdFromToken();
            if (userID == collection.UserID)
            {
                userOwns = true;
            }
            return userOwns;
        }

        private bool CheckUserRole(int userId)
        {
            bool userIsPremium = false;
            User user = userDAO.GetUser(userId);
            string userRole = user.Role;
            if (userRole == "premium")
            {
                userIsPremium = true;
            }
            return userIsPremium;
        }



        [HttpGet("collection")]
        public ActionResult<List<Collection>> ListOfCollection()
        {
            int userID = GetUserIdFromToken();
            List<Collection> collections = collectionDAO.GetAllUserCollections(userID);
            return Ok(collections);
        }

        [HttpPost("collection")]
        public ActionResult<Collection> CreateCollection(Collection collection)
        {
            collection.UserID = GetUserIdFromToken();
            collectionDAO.CreateCollection(collection);
            return Created($"/user/collection/{collection.CollectionID}", collection);
        }

        [HttpGet("collection/{id}")]
        public ActionResult<List<ComicBook>> ComicsInCollection(int id)
        {
            if (VerifyActiveUserOwnsCollection(id))
            {
                List<ComicBook> comicsInCollection = comicDAO.ComicsInCollection(id);
                foreach (ComicBook comic in comicsInCollection)
                {
                    comic.Characters = characterDAO.GetCharacterListForComicBook(comic.Id);
                    comic.Creators = creatorDAO.GetComicCreators(comic.Id);
                }
                return Ok(comicsInCollection);
            }
            else
            {
                return Unauthorized(new { message = "Not owner of collection" });
            }
        }

        [HttpPost("collection/{id}")]
        public async Task<ActionResult<ComicBook>> AddComicToCollection(int id, ComicBook comicBook)
        {
            int userId = GetUserIdFromToken();
            if (VerifyActiveUserOwnsCollection(id))
            {
                if (CheckUserRole(userId) || collectionDAO.UserTotalComicCount(userId) < 100  )
                {
                    try
                    {
                        ComicBook existing = comicDAO.GetById(comicBook.Id);

                        // Comic book is not in local database, get from API
                        if (existing == null)
                        {
                            ComicVineFilters filters = new ComicVineFilters();
                            filters.AddFilter("id", comicBook.Id.ToString());
                            ComicVineIssueResponse response = await comicVine.GetIssues(filters);
                            if (response.StatusCode != 1)
                            {
                                throw new ComicVineException($"Failed ComicVine request: {response.Error}");
                            }
                            ComicBook issue = response.Results[0];
                            CVSingleIssueResponse issueCharAndCreators = await comicVine.GetIssueDetails(issue.ApiDetailUrl);
                            List<Character> characters = issueCharAndCreators.Results.CharacterCredits;
                            characterDAO.CheckDatabaseForCharacters(characters);
                            List<Creator> creators = issueCharAndCreators.Results.PersonCredits;
                            creatorDAO.CheckDatabaseForCreators(creators);
                            

                            using(TransactionScope scope = new TransactionScope())
                            {
                                bool addedComic = comicDAO.AddComic(issue);
                                bool addedImages = comicDAO.AddImages(issue);
                                for (int i = 0; i < characters.Count; i++)
                                {
                                    if (!characters[i].InDatabase)
                                    {
                                        bool addedChar = characterDAO.AddCharacterToTable(characters[i]);
                                       
                                        if (!addedChar)
                                        {
                                            throw new Exception("Failed to add character from ComicVine API");
                                        }

                                    }
                                    bool addedCharToLinker = characterDAO.LinkCharacterToComic(characters[i].Id, issue.Id);
                                    if (!addedCharToLinker)
                                    {
                                        throw new Exception("Failed to add character to linker table");
                                    }
                                }

                                for(int i = 0; i < creators.Count; i++)
                                {
                                    if (!creators[i].InDatabase)
                                    {
                                        bool addedCreator = creatorDAO.AddCreatorCreditToTable(creators[i]);
                                       
                                        if (!addedCreator)
                                        {
                                            throw new Exception("Failed to add creator credit from ComicVine API");
                                        }
                                        
                                    }
                                    bool addedCreatorToLinker = creatorDAO.LinkCreatorToComic(creators[i].Id, issue.Id);
                                  
                                    if (!addedCreatorToLinker)
                                    {
                                        throw new Exception("Failed to add creator to linker table");
                                    }
                                }

                                if (addedComic && addedImages)
                                {
                                    scope.Complete();
                                    existing = issue;
                                    comicBook.Characters = characters;
                                    comicBook.Creators = creators;
                                }
                                else
                                {
                                    throw new Exception("Failed to add new comic from ComicVine API");
                                }
                            }
                        }

                        comicDAO.AddComicToCollection(id, existing);

                        return Created($"/user/collection/{id}", comicBook);
                    }
                    catch (ComicVineException e)
                    {
                        return StatusCode(502, new { message = $"Bad Gateway: 502 - {e.Message}" });
                    }
                    catch (Exception e)
                    {
                        return BadRequest(new { message = $"Could not add comic to collection - {e.Message}" });
                    }
                }
                else
                {
                    return BadRequest(new { message = "Need premium status to add more than 100 comics across all your collections." });
                }
            }
            else
            {
                return Unauthorized(new { message = "Not owner of collection" });
            }

        }

        [HttpDelete("collection/{collectionId}/comic/{comicId}")]
        public ActionResult<Collection> DeleteComicFromCollection(int collectionId, int comicId)
        {
            if (VerifyActiveUserOwnsCollection(collectionId))
            {
                try
                { 
                    //get quantity of comic book in collection
                    int totalComicQuantity = comicDAO.GetComicQuantityInCollection(collectionId, comicId);
                    if (totalComicQuantity == 1)
                    {
                        comicDAO.DeleteComicFromCollection(collectionId, comicId);
                        return Ok();
                    }
                    else
                    {
                        comicDAO.UpdateQuantityOfComicInCollection(collectionId, comicId, totalComicQuantity-1);
                        return Ok();
                    }
                }
                catch(Exception e)
                {
                    return BadRequest(new { message = $"Could not delete comic from collection - {e.Message}" });
                }

            }
            else
            {
                return Unauthorized(new { message = "Not owner of collection" });
            }
        }

        [HttpPut("collection/{id}")]
        public ActionResult<Collection> UpdateCollectionPrivacy(int id, Collection collection)
        {
            Collection compareCollection = collectionDAO.GetSingleCollection(id);
            collection.UserID = compareCollection.UserID;
            int userID = GetUserIdFromToken();
            if (userID == collection.UserID)
            {
                int privacyChange = 0;
                if (collection.Public)
                {
                    privacyChange = 1;
                }
                try
                {
                    bool isSuccessful = collectionDAO.UpdateCollectionPrivacy(collection, privacyChange);
                    if (!isSuccessful)
                    {
                        return BadRequest(new { message = "Failed to update collection" });
                    }
                    return Created($"/user/collection/{collection.CollectionID}", collection);
                }
                catch (Exception)
                {
                    return BadRequest(new { message = "Could not update collection privacy" });
                }
            }
            else
            {
                return Unauthorized(new { message = "Unauthorized - Not user collection" });
            }
        }

    }

}
