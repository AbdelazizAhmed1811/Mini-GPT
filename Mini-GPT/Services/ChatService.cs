﻿
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Mini_GPT.Data;
using Mini_GPT.DTOs.Chat;
using Mini_GPT.DTOs.Messages;
using Mini_GPT.Interfaces;
using Mini_GPT.Mappers;
using Mini_GPT.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Claims;


namespace Mini_GPT.Services
{
    public class ChatService : IChatService
    {
        private readonly IMongoCollection<Chat> _chatCollection;
        private readonly ILlmService _llmService;
        private readonly UserManager<AppUser> _userManager;

        public ChatService(MongoDbContext mongoDbContext, ILlmService llmService, UserManager<AppUser> userManager)
        {
            _chatCollection = mongoDbContext.ChatCollection;
            _llmService = llmService;
            _userManager = userManager;
        }

        public async Task<Chat> CreateChatAsync(string prompt, string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new Exception("User ID not found.");
            }

            // Create the first message with the prompt and GPT response

            var message = new Message
            {
                Prompt = prompt,
                Response = await GenerateGptResponseAsync(prompt),
                CreatedAt = DateTime.UtcNow
            };

            // Initialize a new chat with the user's ID and the first message
            var chat = new Chat
            {
                UserId = userId, // Store the authenticated user's ID here
                Messages = new List<Message> { message }

            };

            try
            {
                // Insert the chat into the MongoDB collection

                await _chatCollection.InsertOneAsync(chat);
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred while creating the chat.", ex);
            }


            return chat;
        }


        public async Task<Message> SendPromptAsync(string chatId, string prompt)
        {
            // Create a new message
            var message = new Message
            {
                Prompt = prompt,
                Response = await GenerateGptResponseAsync(prompt),
                CreatedAt = DateTime.UtcNow
            };

            // Define the filter to find the chat by ID
            var filter = Builders<Chat>.Filter.Eq(c => c.ChatId, chatId);

            // Define the update operation to add the new message to the chat
            var update = Builders<Chat>.Update.Push(c => c.Messages, message);

            // Find the chat and update it by pushing the new message to the Messages list
            var result = await _chatCollection.FindOneAndUpdateAsync(filter, update);

            if (result == null)
            {
                throw new Exception("Chat not found");
            }

            return message;
        }


        public async Task<Message> EditPromptAsync(string chatId, Message message, string prompt)
        {
            // Filter to find the chat by chatId and the specific message by messageId
            var filter = Builders<Chat>.Filter.Eq(c => c.ChatId, chatId) &
                         Builders<Chat>.Filter.ElemMatch(c => c.Messages, m => m.MessageId == message.MessageId);

            // Simulate GPT response generation
            var updatedResponse = await GenerateGptResponseAsync(prompt); // Replace with actual GPT call

            // Update both the prompt and response in a single operation using the positional operator ($)
            var update = Builders<Chat>.Update
                .Set("Messages.$.Prompt", prompt) // Use the positional operator to update the correct message
                .Set("Messages.$.Response", updatedResponse)
                .Set("Messages.$.CreatedAt", DateTime.UtcNow); // Ensure the correct fields are updated at once

            // Perform the update operation
            var result = await _chatCollection.UpdateOneAsync(filter, update);

            if (result.ModifiedCount == 0)
            {
                throw new Exception("Message not found or update failed.");
            }
            message.Prompt = prompt;
            message.Response = updatedResponse;
            message.CreatedAt = DateTime.UtcNow;
            // Return the updated message directly
            return message;
        }



        public async Task<bool> DeleteChatAsync(string chatId)
        {
            var filter = Builders<Chat>.Filter.Eq(c => c.ChatId, chatId);
            var chat = await _chatCollection.Find(filter).FirstOrDefaultAsync();

            if (chat == null)
            {
                return false;
            }

           var _deletedChatCollection =  _chatCollection.Database.GetCollection<Chat>("deletedChats");
            await _deletedChatCollection.InsertOneAsync(chat);
           var deletedResult =  await _chatCollection.DeleteOneAsync(filter);

           

            return deletedResult.DeletedCount > 0;
        }

        public async Task<Chat> GetChatAsync(string chatId)
        {
            var filter = Builders<Chat>.Filter.Eq(c => c.ChatId, chatId);
            return await _chatCollection.Find(filter).FirstOrDefaultAsync();
        }


        private async Task<string> GenerateGptResponseAsync(string prompt)
        {
            var response = await _llmService.GetLlmResponseAsync(prompt);
            return response;
        }

        public async Task<List<Chat>> GetAllUserChats(string userId)
        {
            var filter = Builders<Chat>.Filter.Eq(c => c.UserId, userId);
            return await _chatCollection.Find(filter).ToListAsync();
        }

    }
}


